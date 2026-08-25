using System;
using System.Collections.Generic;
using System.Linq;

namespace TerrariumGladiators;

public enum Side { White, Black }
public enum PieceKind { Pawn, Knight, Bishop, Rook, Queen, King, Trishop }
public enum MovementPlane { XY, XZ, YZ }
public enum MoveOutcome { Safe, CraterSurvived, Excavation, Fatal }

public readonly record struct Int3(int X, int Y, int Z)
{
    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator *(Int3 a, int n) => new(a.X * n, a.Y * n, a.Z * n);
}

public sealed class ChessPiece
{
    public int Id { get; init; }
    public Side Side { get; init; }
    public PieceKind Kind { get; set; }
    public Int3 Position { get; set; }
    public bool HasMoved { get; set; }
    public bool Promoted { get; set; }

    public ChessPiece Clone() => new()
    {
        Id = Id, Side = Side, Kind = Kind, Position = Position, HasMoved = HasMoved, Promoted = Promoted
    };
}

public sealed record PieceFallEvent(int PieceId, Side Side, PieceKind Kind, Int3 From, Int3 To,
    bool Perished, bool StartsWithMove = false);

public sealed class TerrariumModel
{
    private sealed record TowerTransport(List<(ChessPiece Piece, Int3 Destination)> Members, int KnockedOff);

    private sealed record Snapshot(bool[,,] Solids, bool[,,] Mines, HashSet<Int3> RevealedClues, HashSet<Int3> CavernProtected, HashSet<Int3> DisturbedTerrain, List<ChessPiece> Pieces, Side Turn,
        MovementPlane Plane, Side? Winner, string Message, int? SelectedId,
        int? EnPassantPawnId, Int3? EnPassantTarget, int? PendingPromotionPieceId);

    private readonly Stack<Snapshot> _history = new();
    private int _nextId;

    public bool[,,] Solids { get; private set; } = new bool[8, 8, 16];
    public bool[,,] Mines { get; private set; } = new bool[8, 8, 16];
    public HashSet<Int3> RevealedClues { get; private set; } = new();
    public bool MinesweeperEnabled { get; private set; }
    private HashSet<Int3> CavernProtected { get; set; } = new();
    private HashSet<Int3> DisturbedTerrain { get; set; } = new();
    public List<ChessPiece> Pieces { get; private set; } = new();
    public Side Turn { get; private set; }
    public MovementPlane Plane { get; private set; }
    public Side? Winner { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public int? SelectedId { get; private set; }
    public List<PieceFallEvent> LastFalls { get; } = new();
    public int? PendingPromotionPieceId { get; private set; }
    private int? EnPassantPawnId { get; set; }
    private Int3? EnPassantTarget { get; set; }
    public ChessPiece? Selected => Pieces.FirstOrDefault(p => p.Id == SelectedId);

    public TerrariumModel() => Reset();

    private TerrariumModel(bool initialize)
    {
        if (initialize) Reset();
    }

    internal TerrariumModel CloneForSimulation() => new(false)
    {
        Solids = (bool[,,])Solids.Clone(),
        Mines = (bool[,,])Mines.Clone(),
        RevealedClues = new HashSet<Int3>(RevealedClues),
        CavernProtected = new HashSet<Int3>(CavernProtected),
        DisturbedTerrain = new HashSet<Int3>(DisturbedTerrain),
        MinesweeperEnabled = MinesweeperEnabled,
        Pieces = Pieces.Select(piece => piece.Clone()).ToList(),
        Turn = Turn,
        Plane = Plane,
        Winner = Winner,
        Message = Message,
        SelectedId = SelectedId,
        EnPassantPawnId = EnPassantPawnId,
        EnPassantTarget = EnPassantTarget,
        PendingPromotionPieceId = PendingPromotionPieceId,
        _nextId = _nextId
    };

    public void Reset()
    {
        Solids = new bool[8, 8, 16];
        for (var x = 0; x < 8; x++)
        for (var y = 0; y < 8; y++)
        for (var z = 0; z < 8; z++)
            Solids[x, y, z] = true;

        if (MinesweeperEnabled) GenerateMinefield();
        else { Mines = new bool[8, 8, 16]; RevealedClues = new HashSet<Int3>(); CavernProtected = new HashSet<Int3>(); DisturbedTerrain = new HashSet<Int3>(); }

        Pieces = new List<ChessPiece>();
        _nextId = 1;
        AddArmy(Side.White, 0, 1);
        AddArmy(Side.Black, 7, 6);
        Turn = Side.White;
        Plane = MovementPlane.XY;
        Winner = null;
        SelectedId = null;
        EnPassantPawnId = null;
        EnPassantTarget = null;
        PendingPromotionPieceId = null;
        Message = "White to move — select a piece, then a glowing cell.";
        LastFalls.Clear();
        _history.Clear();
    }

    public void SetMinesweeperEnabled(bool enabled)
    {
        MinesweeperEnabled = enabled;
        Reset();
    }

    private void GenerateMinefield()
    {
        Mines = new bool[8, 8, 16];
        RevealedClues = new HashSet<Int3>(); CavernProtected = new HashSet<Int3>(); DisturbedTerrain = new HashSet<Int3>();
        var random = new Random(unchecked((int)0x54475233));
        for (var x = 0; x < 8; x++) for (var y = 0; y < 8; y++) for (var z = 0; z < 7; z++)
            Mines[x, y, z] = random.NextDouble() < .12;
        for (var x = -1; x <= 8; x++) for (var y = -1; y <= 8; y++) for (var z = -1; z <= 16; z++)
            if (x is < 0 or > 7 || y is < 0 or > 7 || z is < 0 or > 15) RevealedClues.Add(new Int3(x, y, z));
        CarveZeroCaverns();
        for (var x = 0; x < 8; x++) for (var y = 0; y < 8; y++) for (var z = 0; z < 8; z++)
        {
            if (!Solids[x, y, z]) continue;
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
            {
                var n = new Int3(x + dx, y + dy, z + dz);
                if (n.X is >= 0 and < 8 && n.Y is >= 0 and < 8 && n.Z is >= 0 and < 7 && !Solids[n.X, n.Y, n.Z]) CavernProtected.Add(new Int3(x, y, z));
            }
        }
    }

    public bool IsMine(Int3 p) => IsInside(p) && Mines[p.X, p.Y, p.Z];

    public int? ClueAt(Int3 p)
    {
        if (p.X is < -1 or > 8 || p.Y is < -1 or > 8 || p.Z is < -1 or > 16 || IsMine(p)) return null;
        var count = 0;
        for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
            if ((dx != 0 || dy != 0 || dz != 0) && IsMine(p + new Int3(dx, dy, dz))) count++;
        return count;
    }

    private void CarveZeroCaverns()
    {
        var zeros = new HashSet<Int3>();
        for (var x = 0; x < 8; x++) for (var y = 0; y < 8; y++) for (var z = 0; z < 7; z++)
        {
            var p = new Int3(x, y, z);
            if (!IsMine(p) && ClueAt(p) == 0) zeros.Add(p);
        }
        var visited = new HashSet<Int3>();
        foreach (var start in zeros)
        {
            if (!visited.Add(start)) continue;
            var queue = new Queue<Int3>(); queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
            {
                Solids[cell.X, cell.Y, cell.Z] = false; RevealedClues.Add(cell);
                for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    var n = cell + new Int3(dx, dy, dz);
                    if (n.X is < 0 or > 7 || n.Y is < 0 or > 7 || n.Z is < 0 or >= 7 || IsMine(n)) continue;
                    Solids[n.X, n.Y, n.Z] = false; RevealedClues.Add(n);
                    if (zeros.Contains(n) && visited.Add(n)) queue.Enqueue(n);
                }
            }
        }
    }

    private void AddArmy(Side side, int homeY, int pawnY)
    {
        PieceKind[] back =
        [
            PieceKind.Rook, PieceKind.Knight, PieceKind.Bishop, PieceKind.Queen,
            PieceKind.King, PieceKind.Bishop, PieceKind.Knight, PieceKind.Rook
        ];
        for (var x = 0; x < 8; x++)
        {
            Pieces.Add(new ChessPiece { Id = _nextId++, Side = side, Kind = back[x], Position = new Int3(x, homeY, 8) });
            Pieces.Add(new ChessPiece { Id = _nextId++, Side = side, Kind = PieceKind.Pawn, Position = new Int3(x, pawnY, 8) });
        }
    }

    public void SetPlane(MovementPlane plane)
    {
        Plane = plane;
        Message = $"Movement plane rotated to {plane}.";
    }

    public bool Select(int id)
    {
        var piece = Pieces.FirstOrDefault(p => p.Id == id);
        if (piece is null || Winner is not null) return false;
        if (piece.Side != Turn)
        {
            Message = $"It is {Turn}'s turn.";
            return false;
        }
        SelectedId = id;
        Message = $"{piece.Side} {piece.Kind} selected on {Plane}.";
        return true;
    }

    public void ClearSelection() => SelectedId = null;

    internal void SetMessage(string message) => Message = message;

    public IReadOnlyList<Int3> LegalMoves(ChessPiece? piece = null)
    {
        piece ??= Selected;
        if (piece is null || Winner is not null) return Array.Empty<Int3>();

        var result = new List<Int3>();
        var (a, b) = PlaneAxes(Plane);
        var tri = SpaceDiagonalDirections();
        switch (piece.Kind)
        {
            case PieceKind.Rook:
                AddSliding(piece, result, [a, a * -1, b, b * -1]);
                if (piece.Promoted) AddSliding(piece, result, tri);
                break;
            case PieceKind.Bishop:
                AddSliding(piece, result, [a + b, a + b * -1, a * -1 + b, a * -1 + b * -1]);
                if (piece.Promoted) AddSliding(piece, result, tri);
                break;
            case PieceKind.Queen:
                AddSliding(piece, result,
                [
                    a, a * -1, b, b * -1,
                    a + b, a + b * -1, a * -1 + b, a * -1 + b * -1
                ]);
                if (piece.Promoted) AddSliding(piece, result, tri);
                break;
            case PieceKind.Trishop:
                AddSliding(piece, result, tri);
                break;
            case PieceKind.King:
                AddStepping(piece, result,
                [
                    a, a * -1, b, b * -1,
                    a + b, a + b * -1, a * -1 + b, a * -1 + b * -1
                ]);
                if (piece.Promoted) AddStepping(piece, result, tri);
                AddCastlingMoves(piece, result);
                break;
            case PieceKind.Knight:
                AddStepping(piece, result,
                [
                    a * 2 + b, a * 2 + b * -1, a * -2 + b, a * -2 + b * -1,
                    b * 2 + a, b * 2 + a * -1, b * -2 + a, b * -2 + a * -1
                ]);
                if (piece.Promoted) AddStepping(piece, result, SpaceKnightOffsets());
                break;
            case PieceKind.Pawn:
                AddPawnMoves(piece, result);
                break;
        }
        return result.Distinct().Where(target => CanTransportTower(piece, target)).ToList();
    }

    private static Int3[] SpaceDiagonalDirections() =>
        (from x in new[] { -1, 1 } from y in new[] { -1, 1 } from z in new[] { -1, 1 } select new Int3(x, y, z)).ToArray();

    private static Int3[] SpaceKnightOffsets()
    {
        var result = new List<Int3>();
        for (var axis = 0; axis < 3; axis++) foreach (var two in new[] { -2, 2 }) foreach (var a in new[] { -1, 1 }) foreach (var b in new[] { -1, 1 })
        {
            var values = new List<int> { a, b }; values.Insert(axis, two);
            result.Add(new Int3(values[0], values[1], values[2]));
        }
        return result.ToArray();
    }

    public IReadOnlyList<string> ScoutPatterns(ChessPiece? piece = null)
    {
        piece ??= Selected; if (piece is null) return Array.Empty<string>(); var result = new List<string>();
        if (piece.Kind == PieceKind.Pawn) { result.Add("advance"); if (Plane != MovementPlane.XZ) result.Add("capture"); }
        if (piece.Kind is PieceKind.Rook or PieceKind.Queen or PieceKind.King) result.Add("orthogonal");
        if (piece.Kind is PieceKind.Bishop or PieceKind.Queen or PieceKind.King) result.Add("plane-diagonal");
        if (piece.Kind == PieceKind.Knight) result.Add("knight-012");
        if (piece.Kind == PieceKind.Trishop || piece.Promoted && piece.Kind is PieceKind.Rook or PieceKind.Bishop or PieceKind.Queen or PieceKind.King) result.Add("space-diagonal");
        if (piece.Kind == PieceKind.Knight && piece.Promoted) result.Add("knight-112");
        return result;
    }

    public bool Scout(string pattern)
    {
        var piece = Selected; if (!MinesweeperEnabled || piece is null || !ScoutPatterns(piece).Contains(pattern) || Winner is not null) return false;
        var (a, b) = PlaneAxes(Plane); IEnumerable<Int3> directions = pattern switch
        {
            "orthogonal" => [a, a * -1, b, b * -1],
            "plane-diagonal" => [a + b, a + b * -1, a * -1 + b, a * -1 + b * -1],
            "space-diagonal" => SpaceDiagonalDirections(), "knight-112" => SpaceKnightOffsets(),
            "knight-012" => [a * 2 + b, a * 2 + b * -1, a * -2 + b, a * -2 + b * -1, b * 2 + a, b * 2 + a * -1, b * -2 + a, b * -2 + a * -1],
            "capture" when Plane == MovementPlane.YZ => [new Int3(0, piece.Side == Side.White ? 1 : -1, 1), new Int3(0, piece.Side == Side.White ? 1 : -1, -1)],
            "capture" => [new Int3(1, piece.Side == Side.White ? 1 : -1, 0), new Int3(-1, piece.Side == Side.White ? 1 : -1, 0)],
            _ => [Plane == MovementPlane.XY ? new Int3(0, piece.Side == Side.White ? 1 : -1, 0) : new Int3(0, 0, 1)]
        };
        var leaper = pattern.StartsWith("knight") || piece.Kind == PieceKind.Pawn; var clues = new List<int>();
        foreach (var direction in directions) for (var distance = 1; distance <= 18; distance++)
        {
            var p = piece.Position + direction * distance;
            if (p.X is < -1 or > 8 || p.Y is < -1 or > 8 || p.Z is < -1 or > 16) break;
            if (!IsMine(p)) { RevealedClues.Add(p); if (ClueAt(p) is { } clue) clues.Add(clue); }
            if (leaper) break;
        }
        Message = $"{piece.Side} {piece.Kind} scouted {pattern}: {clues.Count} clues, {clues.Count(n => n > 0)} warned of mines (max {(clues.Count == 0 ? 0 : clues.Max())}). Scouting is free.";
        return true;
    }

    private void AddPawnMoves(ChessPiece piece, List<Int3> result)
    {
        var forward = new Int3(0, piece.Side == Side.White ? 1 : -1, 0);
        var lateral = new Int3(1, 0, 0);

        if (Plane != MovementPlane.XY)
        {
            AddWallPawnMoves(piece, result, forward);
            return;
        }

        var one = piece.Position + forward;
        if (IsEmpty(one))
        {
            result.Add(one);
            var two = one + forward;
            var startingRank = piece.Side == Side.White ? 1 : 6;
            if (!piece.HasMoved && piece.Position.Y == startingRank && IsEmpty(two))
                result.Add(two);
        }

        foreach (var diagonal in new[] { one + lateral, one + lateral * -1 })
        {
            var target = PieceAt(diagonal);
            if (IsInside(diagonal) && target is not null && target.Side != piece.Side)
                result.Add(diagonal);
        }

        if (EnPassantTarget is { } enPassantTarget &&
            Math.Abs(enPassantTarget.X - piece.Position.X) == 1 && enPassantTarget == one + lateral * Math.Sign(enPassantTarget.X - piece.Position.X))
        {
            var vulnerable = EnPassantPawnId is { } pawnId ? Pieces.FirstOrDefault(candidate => candidate.Id == pawnId) : null;
            if (vulnerable is { Kind: PieceKind.Pawn } && vulnerable.Side != piece.Side &&
                vulnerable.Position == new Int3(enPassantTarget.X, piece.Position.Y, piece.Position.Z) && IsEmpty(enPassantTarget))
                result.Add(enPassantTarget);
        }
    }

    private void AddWallPawnMoves(ChessPiece piece, List<Int3> result, Int3 forward)
    {
        // Rotating a pawn into XZ/YZ is a wall action, not an alternate way to
        // walk across a horizontal board. Hops and empty climbs require a wall
        // latch, but an enemy on a YZ diagonal may always be captured.
        var wallLatched = IsWallLatched(piece.Position);
        if (wallLatched)
        {
            var oneUp = piece.Position + new Int3(0, 0, 1);
            if (IsEmpty(oneUp))
            {
                // A straight vertical hop deliberately releases the wall latch.
                // One cell consumes the turn safely; an initial two-cell hop drops
                // hard enough to create a crater when gravity resolves.
                result.Add(oneUp);
                var twoUp = piece.Position + new Int3(0, 0, 2);
                if (!piece.HasMoved && IsEmpty(twoUp)) result.Add(twoUp);
            }
            else if (IsSolid(oneUp))
            {
                // Pawns are the only pieces allowed to excavate directly upward.
                result.Add(oneUp);
            }
        }

        if (Plane != MovementPlane.YZ) return;

        // In YZ, advancing one rank while changing one layer is the actual
        // climb/slide. Empty destinations must still provide a wall latch or a
        // standing surface; enemy occupants may be captured above or below.
        foreach (var dz in new[] { -1, 1 })
        {
            var target = piece.Position + forward + new Int3(0, 0, dz);
            if (!IsInside(target)) continue;
            var occupant = PieceAt(target);
            if (occupant is not null)
            {
                if (occupant.Side != piece.Side) result.Add(target);
            }
            else if (wallLatched && !IsSolid(target) && CanPawnRest(target))
            {
                result.Add(target);
            }
        }
    }

    private List<ChessPiece> TowerAbove(ChessPiece piece)
    {
        var result = new List<ChessPiece>();
        for (var z = piece.Position.Z + 1; z < 16; z++)
        {
            var above = PieceAt(new Int3(piece.Position.X, piece.Position.Y, z));
            if (above is null) break;
            result.Add(above);
        }
        return result;
    }

    private bool CanTransportTower(ChessPiece piece, Int3 target)
    {
        var destination = MoveDestination(piece, target, IsSolid(target));
        var transport = PlanTowerTransport(piece, destination);
        if (transport.Members.Count == 0) return true;
        var carriedIds = transport.Members.Select(move => move.Piece.Id).ToHashSet();
        var occupiedDestinations = new HashSet<Int3> { destination };
        foreach (var (member, carriedTo) in transport.Members)
        {
            if (!IsInside(carriedTo) || IsSolid(carriedTo) || !occupiedDestinations.Add(carriedTo)) return false;
            var occupant = PieceAt(carriedTo);
            if (occupant is not null && !carriedIds.Contains(occupant.Id) &&
                occupant.Id != piece.Id && occupant.Side == member.Side)
                return false;
        }
        return true;
    }

    private TowerTransport PlanTowerTransport(ChessPiece piece, Int3 destination)
    {
        var tower = TowerAbove(piece);
        if (tower.Count == 0) return new TowerTransport([], 0);
        var movingIds = tower.Select(member => member.Id).Append(piece.Id).ToHashSet();
        bool Obstructed(ChessPiece member, Int3 cell, bool landing) => IsSolid(cell) ||
            PieceAt(cell) is { } occupant && !movingIds.Contains(occupant.Id) &&
            (!landing || occupant.Side == member.Side);

        var delta = destination - piece.Position;
        if (delta == new Int3(0, 0, 0))
            return new TowerTransport(tower.Select(member => (member, member.Position)).ToList(), 0);

        if (piece.Kind == PieceKind.Knight)
        {
            // A knight jumps rather than sweeping a path, but the carried
            // section must remain contiguous. It carries members bottom-up
            // until the first translated cell is obstructed; that member and
            // the entire section above it stay in the original column.
            var obstructionIndex = tower.FindIndex(member =>
                Obstructed(member, member.Position + delta, true));
            var members = tower.Select((member, index) =>
            {
                var carried = obstructionIndex < 0 || index < obstructionIndex;
                return (member, carried ? member.Position + delta : member.Position);
            }).ToList();
            var leftBehind = obstructionIndex < 0 ? 0 : tower.Count - obstructionIndex;
            return new TowerTransport(members, leftBehind);
        }

        if (piece.Kind is not (PieceKind.Rook or PieceKind.Bishop or PieceKind.Queen or PieceKind.Trishop))
        {
            var obstructionIndex = tower.FindIndex(member =>
                Obstructed(member, member.Position + delta, true));
            var members = tower.Select((member, index) =>
                (member, obstructionIndex < 0 || index < obstructionIndex
                    ? member.Position + delta
                    : member.Position)).ToList();
            var leftBehind = obstructionIndex < 0 ? 0 : tower.Count - obstructionIndex;
            return new TowerTransport(members, leftBehind);
        }

        // Sliding stacks sweep through every intermediate cell. Any piece or
        // terrain blocks that sweep; at the landing step, only terrain or a
        // same-side piece blocks the carried member. The blocked member and
        // everything above it detach while lower members keep travelling.
        var direction = StepToward(piece.Position, destination);
        var distance = Math.Max(Math.Abs(delta.X), Math.Max(Math.Abs(delta.Y), Math.Abs(delta.Z)));
        var active = tower.ToList();
        var destinations = new Dictionary<int, Int3>();
        var knockedOff = 0;
        for (var step = 1; step <= distance && active.Count > 0; step++)
        {
            var collisionIndex = active.FindIndex(member =>
                Obstructed(member, member.Position + direction * step, step == distance));
            if (collisionIndex < 0) continue;

            for (var index = collisionIndex; index < active.Count; index++)
                destinations[active[index].Id] = active[index].Position + direction * (step - 1);
            knockedOff += active.Count - collisionIndex;
            active.RemoveRange(collisionIndex, active.Count - collisionIndex);
        }
        foreach (var member in active) destinations[member.Id] = member.Position + delta;
        return new TowerTransport(tower.Select(member => (member, destinations[member.Id])).ToList(), knockedOff);
    }

    private void AddCastlingMoves(ChessPiece king, List<Int3> result)
    {
        if (Plane != MovementPlane.XY || king.HasMoved || king.Position.X != 4) return;
        var homeRank = king.Side == Side.White ? 0 : 7;
        if (king.Position.Y != homeRank) return;

        foreach (var direction in new[] { -1, 1 })
        {
            var rookX = direction < 0 ? 0 : 7;
            var rook = PieceAt(new Int3(rookX, king.Position.Y, king.Position.Z));
            if (rook is not { Kind: PieceKind.Rook } || rook.Side != king.Side || rook.HasMoved) continue;

            var clear = true;
            for (var x = king.Position.X + direction; x != rookX; x += direction)
            {
                var cell = new Int3(x, king.Position.Y, king.Position.Z);
                if (!IsEmpty(cell)) { clear = false; break; }
            }
            if (!clear) continue;
            var destination = king.Position + new Int3(direction * 2, 0, 0);
            if (IsEmpty(destination)) result.Add(destination);
        }
    }

    private void AddSliding(ChessPiece piece, List<Int3> result, IEnumerable<Int3> directions)
    {
        foreach (var direction in directions)
        {
            for (var distance = 1; distance < 16; distance++)
            {
                var target = piece.Position + direction * distance;
                if (!IsInside(target)) break;
                if (IsSolid(target))
                {
                    if (CanExcavate(piece, target)) result.Add(target);
                    break;
                }
                var occupant = PieceAt(target);
                if (occupant is null)
                {
                    result.Add(target);
                    continue;
                }
                if (occupant.Side != piece.Side) result.Add(target);
                break;
            }
        }
    }

    private void AddStepping(ChessPiece piece, List<Int3> result, IEnumerable<Int3> offsets)
    {
        foreach (var offset in offsets)
        {
            var target = piece.Position + offset;
            if (!IsInside(target)) continue;
            if (IsSolid(target))
            {
                if (CanExcavate(piece, target)) result.Add(target);
                continue;
            }
            var occupant = PieceAt(target);
            if (occupant is null || occupant.Side != piece.Side) result.Add(target);
        }
    }

    public bool TryMove(Int3 target)
    {
        LastFalls.Clear();
        var piece = Selected;
        if (piece is null || !LegalMoves(piece).Contains(target))
        {
            Message = "That cell is not reachable on the current plane.";
            return false;
        }

        PushHistory();
        var from = piece.Position;
        var events = new List<string>();
        var excavating = IsSolid(target);
        var destination = target;
        var previousEnPassantPawnId = EnPassantPawnId;
        var previousEnPassantTarget = EnPassantTarget;
        EnPassantPawnId = null;
        EnPassantTarget = null;
        var releasesWallLatch = piece.Kind == PieceKind.Pawn && Plane != MovementPlane.XY &&
                                target.X == from.X && target.Y == from.Y && target.Z > from.Z && !excavating;
        var castling = piece.Kind == PieceKind.King && Plane == MovementPlane.XY &&
                       target.Y == from.Y && target.Z == from.Z && Math.Abs(target.X - from.X) == 2;

        if (excavating)
        {
            RemoveTerrain(target, events);
            destination = MoveDestination(piece, target, true);
            events.Add($"{piece.Kind} excavated {CellName(target)}.");
            if (!Pieces.Contains(piece)) { Message = string.Join("  ", events); return FinishMove(piece); }
        }
        else
        {
            var captured = PieceAt(target);
            if (captured is null && piece.Kind == PieceKind.Pawn && previousEnPassantTarget == target &&
                previousEnPassantPawnId is { } vulnerableId)
                captured = Pieces.FirstOrDefault(candidate => candidate.Id == vulnerableId);
            if (captured is not null)
            {
                DestroyPiece(captured, $"{piece.Kind} captured {captured.Kind}.");
                events.Add(previousEnPassantTarget == target
                    ? $"En passant captured {captured.Side} {captured.Kind}."
                    : $"Captured {captured.Side} {captured.Kind}.");
            }
        }

        var transport = PlanTowerTransport(piece, destination);
        var transportedCount = transport.Members.Count(move => move.Piece.Position != move.Destination);
        var movingIds = transport.Members.Select(move => move.Piece.Id).Append(piece.Id).ToHashSet();
        foreach (var (member, carriedTo) in transport.Members.Where(move => move.Piece.Position != move.Destination))
        {
            var collided = PieceAt(carriedTo);
            if (collided is null || movingIds.Contains(collided.Id) || collided.Side == member.Side) continue;
            DestroyPiece(collided, $"{member.Kind} in the moving tower captured {collided.Kind}.");
            events.Add($"Carried {member.Side} {member.Kind} captured {collided.Side} {collided.Kind}.");
        }
        piece.Position = destination;
        piece.HasMoved = true;
        foreach (var (member, carriedTo) in transport.Members)
        {
            var carriedFrom = member.Position;
            member.Position = carriedTo;
            if (carriedFrom == carriedTo) continue;
            member.HasMoved = true;
            LastFalls.Add(new PieceFallEvent(member.Id, member.Side, member.Kind,
                carriedFrom, carriedTo, false, true));
        }
        if (transportedCount > 0)
            events.Add($"{transportedCount + 1}-piece section moved together.");
        if (transport.KnockedOff > 0)
            events.Add(piece.Kind == PieceKind.Knight
                ? $"An obstruction left {transport.KnockedOff} tower piece(s) behind!"
                : $"An obstruction knocked {transport.KnockedOff} piece(s) off the moving tower!");

        if (castling)
        {
            var direction = Math.Sign(target.X - from.X);
            var rookFrom = new Int3(direction < 0 ? 0 : 7, from.Y, from.Z);
            var rook = PieceAt(rookFrom);
            if (rook is not null)
            {
                var rookTo = new Int3(target.X - direction, from.Y, from.Z);
                rook.Position = rookTo;
                rook.HasMoved = true;
                LastFalls.Add(new PieceFallEvent(rook.Id, rook.Side, rook.Kind, rookFrom, rookTo, false, true));
                events.Add(direction > 0 ? "Castled kingside." : "Castled queenside.");
            }
        }
        if (piece.Kind == PieceKind.Pawn && Plane == MovementPlane.XY && from.Z == target.Z &&
            Math.Abs(target.Y - from.Y) == 2)
        {
            EnPassantPawnId = piece.Id;
            EnPassantTarget = new Int3(from.X, (from.Y + target.Y) / 2, from.Z);
        }
        if (Winner is not null) return FinishMove(piece);
        ResolveGravity(events, releasesWallLatch ? piece.Id : null);

        if (Pieces.Contains(piece) && piece.Kind == PieceKind.Pawn && OnEnemyBackRank(piece))
        {
            PendingPromotionPieceId = piece.Id;
            events.Add("Pawn reached the far rank — choose a true-3D promotion.");
        }
        foreach (var candidate in Pieces.Where(p => p.Kind != PieceKind.Pawn && !p.Promoted && OnEnemyBackRank(p)))
        {
            candidate.Promoted = true;
            events.Add($"{candidate.Side} {candidate.Kind} awakened its true-3D movement.");
        }

        var moved = excavating
            ? $"{piece.Side} {piece.Kind} excavated along {Plane}."
            : $"{piece.Side} {piece.Kind}: {CellName(from)} → {CellName(target)} on {Plane}.";
        Message = events.Count == 0 ? moved : moved + "  " + string.Join("  ", events);
        return FinishMove(piece);
    }

    private bool FinishMove(ChessPiece moved)
    {
        SelectedId = null;
        if (Winner is null && PendingPromotionPieceId is null)
            Turn = Turn == Side.White ? Side.Black : Side.White;
        return true;
    }

    public bool Promote(PieceKind kind)
    {
        if (kind is not (PieceKind.Queen or PieceKind.Rook or PieceKind.Bishop or PieceKind.Knight or PieceKind.Trishop) ||
            PendingPromotionPieceId is not { } pieceId) return false;
        var pawn = Pieces.FirstOrDefault(piece => piece.Id == pieceId && piece.Kind == PieceKind.Pawn);
        if (pawn is null) return false;
        pawn.Kind = kind;
        pawn.Promoted = true;
        PendingPromotionPieceId = null;
        Message = $"{pawn.Side} Pawn promoted to {kind} with true-3D movement.";
        if (Winner is null) Turn = Turn == Side.White ? Side.Black : Side.White;
        return true;
    }

    private static bool OnEnemyBackRank(ChessPiece piece) => piece.Side == Side.White ? piece.Position.Y == 7 : piece.Position.Y == 0;

    private bool RemoveTerrain(Int3 p, List<string> events)
    {
        if (!IsSolid(p)) return false;
        Solids[p.X, p.Y, p.Z] = false; DisturbedTerrain.Add(p); RevealedClues.Add(p);
        if (IsMine(p)) DetonateMine(p, events);
        return true;
    }

    private void DetonateMine(Int3 p, List<string> events)
    {
        Mines[p.X, p.Y, p.Z] = false; var casualties = 0;
        for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
        {
            var blast = p + new Int3(dx, dy, dz); RevealedClues.Add(blast);
            foreach (var victim in Pieces.Where(piece => piece.Position == blast).ToList())
            {
                DestroyPiece(victim, $"{victim.Kind} was caught in a mine blast."); casualties++;
            }
        }
        events.Add($"Mine detonated at {CellName(p)}: {casualties} piece(s) hit; terrain outside the dent was untouched.");
    }

    private void ResolveGravity(List<string> events, int? releasedPawnId = null)
    {
        // Work bottom-up until terrain and every piece column are stable.
        for (var pass = 0; pass < 64; pass++)
        {
            // Finish piece fall segments before resolving secondary cave-ins.
            // When an impact destroys its support cell, the survivor is placed
            // in that cell; the next pass therefore measures a fresh fall from
            // the last destroyed cell instead of accumulating the earlier drop.
            var changed = false;
            for (var x = 0; x < 8 && !changed; x++)
            for (var y = 0; y < 8 && !changed; y++)
            {
                var column = Pieces.Where(p => p.Position.X == x && p.Position.Y == y)
                    .OrderBy(p => p.Position.Z).ToList();
                for (var i = 0; i < column.Count; i++)
                {
                    var bottom = column[i];
                    if (bottom.Position.Z == 0 || HasSupport(bottom.Position, releasedPawnId)) continue;

                    var tower = new List<ChessPiece> { bottom };
                    var nextZ = bottom.Position.Z + 1;
                    while (true)
                    {
                        var above = column.FirstOrDefault(p => p.Position.Z == nextZ);
                        if (above is null) break;
                        tower.Add(above);
                        nextZ++;
                    }

                    var supportZ = FindSupportZ(x, y, bottom.Position.Z - 1);
                    var landingZ = supportZ + 1;
                    var fall = bottom.Position.Z - landingZ;
                    if (fall <= 0) continue;

                    if (fall == 1)
                    {
                        foreach (var member in tower)
                        {
                            var from = member.Position;
                            member.Position = member.Position with { Z = member.Position.Z - 1 };
                            LastFalls.Add(new PieceFallEvent(member.Id, member.Side, member.Kind, from, member.Position, false));
                        }
                        events.Add(tower.Count > 1 ? "Tower settled safely by 1 cell." : "Piece settled safely by 1 cell.");
                    }
                    else
                    {
                        var impactPiece = supportZ >= 0 ? PieceAt(new Int3(x, y, supportZ)) : null;
                        var pieceBrokeFall = impactPiece is not null;
                        var impactBaseZ = Math.Max(0, supportZ);
                        if (supportZ >= 0 && Solids[x, y, supportZ])
                        {
                            RemoveTerrain(new Int3(x, y, supportZ), events);
                            events.Add($"Impact shattered cube {CellName(new Int3(x, y, supportZ))}!");
                        }
                        else if (impactPiece is not null)
                        {
                            // The impact travels through a stationary tower. Its top
                            // remains the landing surface while its bottom member is
                            // driven into the supporting terrain and squashed.
                            var stationaryTower = new List<ChessPiece>();
                            for (var scanZ = supportZ; scanZ >= 0; scanZ--)
                            {
                                var member = PieceAt(new Int3(x, y, scanZ));
                                if (member is null) break;
                                stationaryTower.Insert(0, member);
                            }

                            var crushedBottom = stationaryTower[0];
                            stationaryTower.RemoveAt(0);
                            DestroyPiece(crushedBottom, $"The bottom {crushedBottom.Kind} was squashed beneath its tower!");
                            events.Add($"{crushedBottom.Side} {crushedBottom.Kind} at the tower base was squashed!");
                            var craterZ = ExcavateCraterBelow(x, y, crushedBottom.Position.Z - 1, events);
                            var settledBaseZ = craterZ >= 0 ? craterZ : crushedBottom.Position.Z;
                            if (craterZ >= 0)
                            {
                                events.Add($"The squashed piece left a crater at {CellName(new Int3(x, y, craterZ))}!");
                            }

                            // Shift the surviving stationary tower into the new base as
                            // one compression event. It does not suffer a second impact.
                            for (var standingIndex = 0; standingIndex < stationaryTower.Count; standingIndex++)
                            {
                                var member = stationaryTower[standingIndex];
                                var from = member.Position;
                                member.Position = new Int3(x, y, Math.Min(15, settledBaseZ + standingIndex));
                                if (from != member.Position)
                                    LastFalls.Add(new PieceFallEvent(member.Id, member.Side, member.Kind, from, member.Position, false));
                            }

                            // The falling piece (or falling stack) lands immediately on
                            // top of the surviving stationary tower.
                            impactBaseZ = settledBaseZ + stationaryTower.Count;
                        }

                        // Towers always lose their bottom piece on a long drop. A lone piece
                        // also perishes at 4+ cells unless another piece absorbed the impact.
                        var baseZ = impactBaseZ;
                        if (!pieceBrokeFall && (tower.Count > 1 || fall >= 4))
                        {
                            var casualty = tower[0];
                            LastFalls.Add(new PieceFallEvent(casualty.Id, casualty.Side, casualty.Kind,
                                casualty.Position, new Int3(x, y, baseZ), true));
                            DestroyPiece(casualty, tower.Count > 1
                                ? $"The bottom {casualty.Kind} perished in a long tower fall."
                                : $"{casualty.Kind} perished in a {fall}-cell fall.");
                            tower.RemoveAt(0);
                            events.Add(tower.Count > 0 ? "The tower's bottom piece perished." : $"The falling piece perished after {fall} cells.");
                        }

                        for (var t = 0; t < tower.Count; t++)
                        {
                            var from = tower[t].Position;
                            tower[t].Position = new Int3(x, y, Math.Min(15, baseZ + t));
                            LastFalls.Add(new PieceFallEvent(tower[t].Id, tower[t].Side, tower[t].Kind,
                                from, tower[t].Position, false));
                        }
                    }
                    if (releasedPawnId is { } pawnId && tower.Any(member => member.Id == pawnId))
                        releasedPawnId = null;
                    changed = true;
                    break;
                }
            }
            if (!changed) changed = ResolveTerrainGravity(events);
            if (!changed || Winner is not null) break;
        }
    }

    private bool ResolveTerrainGravity(List<string> events)
    {
        // A flat solid 3x3 shelf with an entirely empty 3x3 layer beneath it
        // loses its center cube to gravity. One cell is resolved per pass so
        // larger collapses cascade predictably.
        for (var z = 1; z < 16; z++)
        for (var x = 1; x < 7; x++)
        for (var y = 1; y < 7; y++)
        {
            var flatPatch = true;
            var supportedBelow = false;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            {
                flatPatch &= Solids[x + dx, y + dy, z];
                supportedBelow |= Solids[x + dx, y + dy, z - 1];
            }
            if (!flatPatch || supportedBelow) continue;
            var center = new Int3(x, y, z);
            var disturbedNearby = DisturbedTerrain.Any(p => Math.Max(Math.Max(Math.Abs(p.X - x), Math.Abs(p.Y - y)), Math.Abs(p.Z - z)) <= 1);
            if (CavernProtected.Contains(center) && !disturbedNearby) continue;

            RemoveTerrain(center, events);
            var hitZ = -1;
            ChessPiece? hitPiece = null;
            for (var scan = z - 1; scan >= 0; scan--)
            {
                hitPiece = PieceAt(new Int3(x, y, scan));
                if (hitPiece is not null || Solids[x, y, scan])
                {
                    hitZ = scan;
                    break;
                }
            }

            int landingZ;
            if (hitPiece is not null)
            {
                DestroyPiece(hitPiece, $"{hitPiece.Kind} was squashed by a falling cube cell.");
                events.Add($"Falling terrain squashed {hitPiece.Side} {hitPiece.Kind}!");
                var craterZ = ExcavateCraterBelow(x, y, hitZ - 1, events);
                landingZ = craterZ >= 0 ? craterZ : hitZ;
                if (craterZ >= 0)
                    events.Add($"The crushed piece left a crater at {CellName(new Int3(x, y, craterZ))}!");
            }
            else
            {
                landingZ = Math.Max(0, hitZ + 1);
            }
            Solids[x, y, landingZ] = true;
            events.Add($"Unsupported 3×3 shelf collapsed: center cube fell {z - landingZ} cell(s).");
            return true;
        }
        return false;
    }

    private int ExcavateCraterBelow(int x, int y, int startZ, List<string> events)
    {
        for (var z = startZ; z >= 0; z--)
        {
            if (!Solids[x, y, z]) continue;
            RemoveTerrain(new Int3(x, y, z), events);
            return z;
        }
        return -1;
    }

    private bool HasSupport(Int3 p, int? releasedPawnId = null)
    {
        if (p.Z <= 0) return true;
        var below = p with { Z = p.Z - 1 };
        var piece = PieceAt(p);
        return IsSolid(below) || PieceAt(below) is not null ||
               (piece?.Kind == PieceKind.Pawn && piece.Id != releasedPawnId && IsWallLatched(p));
    }

    private bool CanPawnRest(Int3 p)
    {
        if (!IsInside(p)) return false;
        if (p.Z == 0) return true;
        var below = p with { Z = p.Z - 1 };
        return IsSolid(below) || PieceAt(below) is not null || IsWallLatched(p);
    }

    private bool IsWallLatched(Int3 p)
    {
        Int3[] neighbors =
        [
            new(p.X + 1, p.Y, p.Z), new(p.X - 1, p.Y, p.Z),
            new(p.X, p.Y + 1, p.Z), new(p.X, p.Y - 1, p.Z)
        ];
        return neighbors.Any(IsSolid);
    }

    private int FindSupportZ(int x, int y, int startZ, int? excludedPieceId = null)
    {
        for (var z = startZ; z >= 0; z--)
        {
            if (Solids[x, y, z]) return z;
            var occupant = PieceAt(new Int3(x, y, z));
            if (occupant is not null && occupant.Id != excludedPieceId) return z;
        }
        return -1;
    }

    private void DestroyPiece(ChessPiece piece, string reason)
    {
        Pieces.Remove(piece);
        if (piece.Kind == PieceKind.King)
        {
            Winner = piece.Side == Side.White ? Side.Black : Side.White;
            Message = reason + $"  {Winner} wins!";
        }
    }

    public bool Undo()
    {
        if (_history.Count == 0) return false;
        var snapshot = _history.Pop();
        Solids = (bool[,,])snapshot.Solids.Clone();
        Mines = (bool[,,])snapshot.Mines.Clone();
        RevealedClues = new HashSet<Int3>(snapshot.RevealedClues);
        CavernProtected = new HashSet<Int3>(snapshot.CavernProtected);
        DisturbedTerrain = new HashSet<Int3>(snapshot.DisturbedTerrain);
        Pieces = snapshot.Pieces.Select(p => p.Clone()).ToList();
        Turn = snapshot.Turn;
        Plane = snapshot.Plane;
        Winner = snapshot.Winner;
        Message = "Move undone.";
        SelectedId = snapshot.SelectedId;
        EnPassantPawnId = snapshot.EnPassantPawnId;
        EnPassantTarget = snapshot.EnPassantTarget;
        PendingPromotionPieceId = snapshot.PendingPromotionPieceId;
        LastFalls.Clear();
        return true;
    }

    private void PushHistory() => _history.Push(new Snapshot((bool[,,])Solids.Clone(), (bool[,,])Mines.Clone(),
        new HashSet<Int3>(RevealedClues), new HashSet<Int3>(CavernProtected), new HashSet<Int3>(DisturbedTerrain), Pieces.Select(p => p.Clone()).ToList(), Turn, Plane, Winner, Message, SelectedId,
        EnPassantPawnId, EnPassantTarget, PendingPromotionPieceId));

    public ChessPiece? PieceAt(Int3 p) => Pieces.FirstOrDefault(piece => piece.Position == p);
    public bool IsSolid(Int3 p) => IsInside(p) && Solids[p.X, p.Y, p.Z];
    public bool IsEmpty(Int3 p) => IsInside(p) && !IsSolid(p) && PieceAt(p) is null;
    public static bool IsInside(Int3 p) => p.X is >= 0 and < 8 && p.Y is >= 0 and < 8 && p.Z is >= 0 and < 16;

    public int SurfaceZ(int x, int y)
    {
        for (var z = 15; z >= 0; z--)
            if (Solids[x, y, z]) return z;
        return -1;
    }

    public int PredictedFall(Int3 p)
    {
        if (!IsInside(p)) return 0;
        var support = FindSupportZ(p.X, p.Y, p.Z - 1, SelectedId);
        return Math.Max(0, p.Z - (support + 1));
    }

    public MoveOutcome PredictOutcome(Int3 target)
    {
        if (IsExcavationTarget(target)) return MoveOutcome.Excavation;
        var moving = Selected;
        var releasesWallLatch = moving is { Kind: PieceKind.Pawn } && Plane != MovementPlane.XY &&
                                target.X == moving.Position.X && target.Y == moving.Position.Y &&
                                target.Z > moving.Position.Z;
        if (moving is { Kind: PieceKind.Pawn } && !releasesWallLatch && CanPawnRest(target))
            return MoveOutcome.Safe;
        var supportZ = FindSupportZ(target.X, target.Y, target.Z - 1, SelectedId);
        var fall = Math.Max(0, target.Z - (supportZ + 1));
        if (fall < 2) return MoveOutcome.Safe;

        // A 2–3 cell impact creates a crater but is survivable. At 4+ cells,
        // only another piece at the impact point can break the fall.
        var impactPiece = supportZ >= 0 ? PieceAt(new Int3(target.X, target.Y, supportZ)) : null;
        var pieceBreaksFall = impactPiece is not null && impactPiece.Id != SelectedId;
        var movingTower = moving is not null &&
                          PlanTowerTransport(moving, target).Members.Any(move =>
                              move.Destination == target + new Int3(0, 0, 1));
        return !pieceBreaksFall && (movingTower || fall >= 4)
            ? MoveOutcome.Fatal
            : MoveOutcome.CraterSurvived;
    }

    public static string CellName(Int3 p) => $"{(char)('A' + p.X)}{p.Y + 1}·{p.Z}";

    public bool IsExcavationTarget(Int3 p) => Selected is { } piece && IsSolid(p) && CanExcavate(piece, p);

    private bool CanExcavate(ChessPiece piece, Int3 target)
    {
        if (piece.Kind != PieceKind.Pawn) return target.Z >= piece.Position.Z;
        return Plane != MovementPlane.XY && IsWallLatched(piece.Position) &&
               target == piece.Position + new Int3(0, 0, 1);
    }

    private static Int3 MoveDestination(ChessPiece piece, Int3 target, bool excavating)
    {
        if (!excavating) return target;
        return piece.Kind switch
        {
            PieceKind.Knight => piece.Position,
            PieceKind.King => target,
            PieceKind.Rook or PieceKind.Bishop or PieceKind.Queen or PieceKind.Trishop => target - StepToward(piece.Position, target),
            _ => piece.Position
        };
    }

    private static Int3 StepToward(Int3 from, Int3 to) => new(
        Math.Sign(to.X - from.X), Math.Sign(to.Y - from.Y), Math.Sign(to.Z - from.Z));

    private static (Int3, Int3) PlaneAxes(MovementPlane plane) => plane switch
    {
        MovementPlane.XY => (new Int3(1, 0, 0), new Int3(0, 1, 0)),
        MovementPlane.XZ => (new Int3(1, 0, 0), new Int3(0, 0, 1)),
        _ => (new Int3(0, 1, 0), new Int3(0, 0, 1))
    };
}
