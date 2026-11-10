const SIGNALING_VERSION = 1;
const CODE_PREFIX = "TG1-";
const MAX_CODE_LENGTH = 50_000;

function encodeCode(value) {
  const bytes = new TextEncoder().encode(JSON.stringify(value));
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return CODE_PREFIX + btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function decodeCode(raw, expectedType) {
  const code = String(raw ?? "").trim().replace(/\s+/g, "");
  if (code.length > MAX_CODE_LENGTH || !code.startsWith(CODE_PREFIX)) {
    throw new Error("That code is too long or is not a Terrarium Gladiators P2P code.");
  }

  try {
    const base64 = code.slice(CODE_PREFIX.length).replaceAll("-", "+").replaceAll("_", "/");
    const binary = atob(base64 + "=".repeat((4 - base64.length % 4) % 4));
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    const value = JSON.parse(new TextDecoder().decode(bytes));
    if (value.version !== SIGNALING_VERSION || value.type !== expectedType ||
        typeof value.sdp !== "string" || value.sdp.length > MAX_CODE_LENGTH) {
      throw new Error("The code has the wrong type or version.");
    }
    return value;
  } catch (error) {
    if (error instanceof Error && error.message.startsWith("The code")) throw error;
    throw new Error("That code could not be read. Copy it again and try once more.");
  }
}

function waitForIceGathering(peer, timeoutMs = 30_000) {
  if (peer.iceGatheringState === "complete") return Promise.resolve();
  return new Promise((resolve, reject) => {
    const finish = (error = null) => {
      clearTimeout(timeout);
      peer.removeEventListener("icegatheringstatechange", check);
      peer.removeEventListener("signalingstatechange", checkClosed);
      if (error) reject(error);
      else resolve();
    };
    const check = () => {
      if (peer.iceGatheringState === "complete") finish();
    };
    const checkClosed = () => {
      if (peer.signalingState === "closed") finish(new Error("The connection attempt was cancelled."));
    };
    const timeout = setTimeout(() => finish(new Error("Network discovery timed out. Check the connection and try again.")), timeoutMs);
    peer.addEventListener("icegatheringstatechange", check);
    peer.addEventListener("signalingstatechange", checkClosed);
    check();
  });
}

export class PeerSession {
  constructor(onMessage, onState) {
    this.onMessage = onMessage;
    this.onState = onState;
    this.peer = null;
    this.channel = null;
    this.generation = 0;
  }

  createPeer() {
    this.close();
    const generation = ++this.generation;
    const peer = new RTCPeerConnection({
      iceServers: [{ urls: "stun:stun.l.google.com:19302" }],
    });
    this.peer = peer;
    peer.addEventListener("connectionstatechange", () => {
      if (generation !== this.generation) return;
      if (peer.connectionState === "failed" || peer.connectionState === "closed") {
        this.onState?.("disconnected");
      }
    });
    peer.addEventListener("datachannel", event => this.attachChannel(event.channel, generation));
    return { peer, generation };
  }

  attachChannel(channel, generation) {
    this.channel = channel;
    channel.addEventListener("open", () => {
      if (generation === this.generation) this.onState?.("connected");
    });
    channel.addEventListener("close", () => {
      if (generation === this.generation) this.onState?.("disconnected");
    });
    channel.addEventListener("message", event => {
      if (generation !== this.generation) return;
      try {
        this.onMessage?.(JSON.parse(event.data));
      } catch {
        this.onState?.("invalid-message");
      }
    });
  }

  async createInvite() {
    if (!globalThis.isSecureContext) throw new Error("P2P needs HTTPS or localhost. Open the browser game over a secure connection.");
    if (!globalThis.RTCPeerConnection) throw new Error("This browser does not support direct P2P connections.");
    const { peer, generation } = this.createPeer();
    this.attachChannel(peer.createDataChannel("terrarium-gladiators", { ordered: true }), generation);
    const offer = await peer.createOffer();
    await peer.setLocalDescription(offer);
    await waitForIceGathering(peer);
    if (generation !== this.generation || !peer.localDescription) throw new Error("The connection attempt was cancelled.");
    return encodeCode({ version: SIGNALING_VERSION, type: "offer", sdp: peer.localDescription.sdp });
  }

  async createResponse(inviteCode) {
    if (!globalThis.isSecureContext) throw new Error("P2P needs HTTPS or localhost. Open the browser game over a secure connection.");
    if (!globalThis.RTCPeerConnection) throw new Error("This browser does not support direct P2P connections.");
    const invite = decodeCode(inviteCode, "offer");
    const { peer, generation } = this.createPeer();
    await peer.setRemoteDescription({ type: "offer", sdp: invite.sdp });
    const answer = await peer.createAnswer();
    await peer.setLocalDescription(answer);
    await waitForIceGathering(peer);
    if (generation !== this.generation || !peer.localDescription) throw new Error("The connection attempt was cancelled.");
    return encodeCode({ version: SIGNALING_VERSION, type: "answer", sdp: peer.localDescription.sdp });
  }

  async acceptResponse(responseCode) {
    if (!this.peer) throw new Error("Create a host invite before accepting a response.");
    const response = decodeCode(responseCode, "answer");
    await this.peer.setRemoteDescription({ type: "answer", sdp: response.sdp });
    this.onState?.("connecting");
  }

  send(message) {
    if (this.channel?.readyState !== "open") return false;
    this.channel.send(JSON.stringify(message));
    return true;
  }

  close() {
    this.generation++;
    const channel = this.channel;
    const peer = this.peer;
    this.channel = null;
    this.peer = null;
    if (channel) {
      channel.onopen = null;
      channel.onclose = null;
      channel.onmessage = null;
      try { channel.close(); } catch { /* The browser may already have closed it. */ }
    }
    if (peer) {
      peer.onconnectionstatechange = null;
      try { peer.close(); } catch { /* The browser may already have closed it. */ }
    }
  }
}
