using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TerrariumGladiators;

public readonly record struct RenderPiece(ChessPiece Piece, Vector3 Position, float Opacity);

public sealed class WorldRenderer : IDisposable
{
    private readonly record struct Triangle(VertexPositionColor A, VertexPositionColor B, VertexPositionColor C, Vector3 Center);

    private readonly GraphicsDevice _graphics;
    private readonly BasicEffect _effect;
    private readonly List<Triangle> _opaque = new(20_000);
    private readonly List<Triangle> _transparent = new(20_000);
    private Viewport _worldViewport;
    private Matrix _view;
    private Matrix _projection;
    private Vector3 _cameraPosition;
    private Vector3 _cameraRight;
    private Vector3 _cameraUp;

    public float Yaw { get; set; }
    public float HeightOffset { get; set; }
    public float ElevationDegrees { get; set; } = 45f;
    public float Zoom { get; set; } = 1f;
    public float CameraZ => _cameraPosition.Z;
    public bool LayerFocus { get; set; }
    public int FocusLayer { get; set; } = 8;
    private int FocusStart => Math.Clamp(FocusLayer, 0, 14);
    private bool InFocusWindow(int z) => z >= FocusStart && z <= FocusStart + 1;

    public WorldRenderer(GraphicsDevice graphics)
    {
        _graphics = graphics;
        _effect = new BasicEffect(graphics)
        {
            VertexColorEnabled = true,
            LightingEnabled = false,
            World = Matrix.Identity
        };
        UpdateCamera();
    }

    public void Draw(TerrariumModel model, bool[,,]? terrainOverride, bool[,,]? mineOverride,
        IReadOnlySet<Int3>? clueOverride, IReadOnlyList<RenderPiece> pieces, IReadOnlyList<Int3> legalMoves)
    {
        UpdateCamera();
        _opaque.Clear();
        _transparent.Clear();

        BuildTerrain(terrainOverride ?? model.Solids);
        BuildLayerGuide();
        BuildClues(model, terrainOverride ?? model.Solids, mineOverride ?? model.Mines, clueOverride ?? model.RevealedClues);
        BuildMoveHints(model, legalMoves);
        foreach (var rendered in pieces.OrderBy(p => Vector3.DistanceSquared(p.Position, _cameraPosition)).Reverse())
            BuildPiece(rendered, rendered.Piece.Id == model.SelectedId);

        var previousViewport = _graphics.Viewport;
        _graphics.Viewport = _worldViewport;
        _effect.View = _view;
        _effect.Projection = _projection;
        _graphics.RasterizerState = RasterizerState.CullNone;

        Flush(_opaque, BlendState.Opaque, DepthStencilState.Default, false);
        Flush(_transparent, BlendState.AlphaBlend, DepthStencilState.DepthRead, true);
        _graphics.Viewport = previousViewport;
    }

    public Vector2 Project(Vector3 world)
    {
        UpdateCamera();
        var projected = _worldViewport.Project(world, _projection, _view, Matrix.Identity);
        return new Vector2(projected.X, projected.Y);
    }

    public Vector2 ProjectTarget(TerrariumModel model, Int3 target)
    {
        UpdateCamera();
        var excavation = model.IsExcavationTarget(target);
        var center = IsTrue3DDestination(model, target) && !excavation
            ? new Vector3(target.X, target.Y, target.Z + .49f)
            : MoveHintGeometry(model, target, excavation).Center;
        var projected = _worldViewport.Project(center, _projection, _view, Matrix.Identity);
        return new Vector2(projected.X, projected.Y);
    }

    private void UpdateCamera()
    {
        _worldViewport = new Viewport(0, 0, 1050, 900);
        var target = new Vector3(3.5f, 3.5f, 7.4f + HeightOffset);
        const float orbitDistance = 18.384777f;
        var elevation = MathHelper.ToRadians(MathHelper.Clamp(ElevationDegrees, -90f, 90f));
        var horizontal = new Vector3(MathF.Sin(Yaw), -MathF.Cos(Yaw), 0);
        var right = new Vector3(MathF.Cos(Yaw), MathF.Sin(Yaw), 0);
        var radial = horizontal * MathF.Cos(elevation) + Vector3.UnitZ * MathF.Sin(elevation);
        _cameraPosition = target + radial * orbitDistance;
        _cameraRight = right;
        _cameraUp = Vector3.Normalize(Vector3.Cross(radial, right));
        _view = Matrix.CreateLookAt(_cameraPosition, target, _cameraUp);
        // Match the former 17.6-unit orthographic framing at the orbit target,
        // then narrow or widen the perspective field of view for zoom.
        var zoom = MathHelper.Clamp(Zoom, .55f, 2.5f);
        var fieldOfView = 2f * MathF.Atan(8.8f / (orbitDistance * zoom));
        _projection = Matrix.CreatePerspectiveFieldOfView(fieldOfView, _worldViewport.AspectRatio, .1f, 80f);
    }

    private void BuildTerrain(bool[,,] solids)
    {
        if (!LayerFocus)
        {
            for (var x = 0; x < 8; x++)
            for (var y = 0; y < 8; y++)
            for (var z = 0; z < 16; z++)
            {
                if (!solids[x, y, z]) continue;
                AddExposedCube(solids, x, y, z, 1f, false);
            }
            return;
        }

        // Layer focus is true isolation: only the chosen Z slice is rendered.
        for (var x = 0; x < 8; x++)
        for (var y = 0; y < 8; y++)
        for (var z = 0; z < 16; z++)
        {
            if (!solids[x, y, z]) continue;
            if (InFocusWindow(z)) AddSliceCube(solids, x, y, z);
        }
    }

    private void AddExposedCube(bool[,,] solids, int x, int y, int z, float opacity, bool transparent)
    {
        var top = TerrainColor(x, y, z, 1f, opacity);
        var sideA = TerrainColor(x, y, z, .68f, opacity);
        var sideB = TerrainColor(x, y, z, .51f, opacity);
        if (!Solid(solids, x, y, z + 1)) AddCellFace(x, y, z, Face.Top, top, transparent);
        if (!Solid(solids, x, y, z - 1)) AddCellFace(x, y, z, Face.Bottom, sideB, transparent);
        if (!Solid(solids, x + 1, y, z)) AddCellFace(x, y, z, Face.East, sideA, transparent);
        if (!Solid(solids, x - 1, y, z)) AddCellFace(x, y, z, Face.West, sideB, transparent);
        if (!Solid(solids, x, y + 1, z)) AddCellFace(x, y, z, Face.South, sideA, transparent);
        if (!Solid(solids, x, y - 1, z)) AddCellFace(x, y, z, Face.North, sideB, transparent);
    }

    private void AddSliceCube(bool[,,] solids, int x, int y, int z)
    {
        var top = TerrainColor(x, y, z, 1.18f, 1f);
        var side = TerrainColor(x, y, z, .78f, 1f);
        AddCellFace(x, y, z, Face.Top, top, false);
        AddCellFace(x, y, z, Face.Bottom, side, false);
        if (!Solid(solids, x + 1, y, z)) AddCellFace(x, y, z, Face.East, side, false);
        if (!Solid(solids, x - 1, y, z)) AddCellFace(x, y, z, Face.West, side, false);
        if (!Solid(solids, x, y + 1, z)) AddCellFace(x, y, z, Face.South, side, false);
        if (!Solid(solids, x, y - 1, z)) AddCellFace(x, y, z, Face.North, side, false);
    }

    private void BuildLayerGuide()
    {
        if (!LayerFocus) return;
        var color = new Color(65, 235, 213, 22);
        for (var layer = FocusStart; layer <= FocusStart + 1; layer++)
        {
            var z = layer + .012f;
            AddQuad(new Vector3(-.55f, -.55f, z), new Vector3(7.55f, -.55f, z),
                new Vector3(7.55f, 7.55f, z), new Vector3(-.55f, 7.55f, z), color, true);
            AddFrame(new Vector3(3.5f, 3.5f, z), new Vector2(8.18f), new Color(93, 255, 229, 155), .035f);
        }
    }

    private void BuildMoveHints(TerrariumModel model, IReadOnlyList<Int3> moves)
    {
        foreach (var target in moves)
        {
            var excavation = model.IsExcavationTarget(target);
            var outcome = model.PredictOutcome(target);
            var color = outcome switch
            {
                MoveOutcome.Excavation or MoveOutcome.CraterSurvived => new Color(255, 185, 72, 210),
                MoveOutcome.Fatal => new Color(255, 76, 86, 215),
                _ => new Color(72, 245, 209, 190)
            };
            if (IsTrue3DDestination(model, target) && !excavation)
            {
                AddBox(new Vector3(target.X, target.Y, target.Z + .49f), new Vector3(.34f), color, true);
                continue;
            }
            var geometry = MoveHintGeometry(model, target, excavation);
            const float halfSize = .39f;
            AddQuad(geometry.Center - geometry.U * halfSize - geometry.V * halfSize,
                geometry.Center + geometry.U * halfSize - geometry.V * halfSize,
                geometry.Center + geometry.U * halfSize + geometry.V * halfSize,
                geometry.Center - geometry.U * halfSize + geometry.V * halfSize, color, true);
            AddPlaneFrame(geometry.Center + geometry.Normal * .006f, geometry.U, geometry.V,
                .78f, Color.Multiply(color, 1.3f), .025f);
        }
    }

    private static bool IsTrue3DDestination(TerrariumModel model, Int3 target)
    {
        if (model.Selected is not { } piece) return false;
        var delta = target - piece.Position;
        return delta.X != 0 && delta.Y != 0 && delta.Z != 0;
    }

    private void BuildClues(TerrariumModel model, bool[,,] solids, bool[,,] mines, IReadOnlySet<Int3> revealedClues)
    {
        foreach (var cell in revealedClues)
        {
            var clue = ClueAt(cell, mines);
            if (clue is null || (!LayerFocus && clue == 0) || (LayerFocus && !InFocusWindow(cell.Z)) ||
                Solid(solids, cell.X, cell.Y, cell.Z)) continue;

            var digits = clue.Value.ToString();
            var scale = digits.Length > 1 ? .68f : 1f;
            var color = clue >= 4 ? new Color(255, 102, 122) :
                clue >= 2 ? new Color(255, 209, 102) : clue == 0 ? new Color(54, 115, 119) : new Color(127, 255, 240);
            for (var index = 0; index < digits.Length; index++)
            {
                var offset = (index - (digits.Length - 1) / 2f) * .38f * scale;
                foreach (var segment in DigitSegments(digits[index]))
                {
                    var (x, y, horizontal) = segment switch
                    {
                        'a' => (0f, .25f, true), 'b' => (.16f, .13f, false),
                        'c' => (.16f, -.13f, false), 'd' => (0f, -.25f, true),
                        'e' => (-.16f, -.13f, false), 'f' => (-.16f, .13f, false),
                        _ => (0f, 0f, true)
                    };
                    var center = new Vector3(cell.X, cell.Y, cell.Z + .49f) +
                                 _cameraRight * (offset + x * scale) + _cameraUp * (y * scale);
                    var size = horizontal ? new Vector3(.30f * scale, .065f * scale, .07f) :
                        new Vector3(.065f * scale, .23f * scale, .07f);
                    AddPlaneBox(center, _cameraRight, _cameraUp, size, color);
                }
            }
        }
    }

    private static int? ClueAt(Int3 cell, bool[,,] mines)
    {
        if (cell.X is < -1 or > 8 || cell.Y is < -1 or > 8 || cell.Z is < -1 or > 16 || MineAt(cell, mines)) return null;
        var count = 0;
        for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++) for (var dz = -1; dz <= 1; dz++)
            if (MineAt(cell + new Int3(dx, dy, dz), mines)) count++;
        return count;
    }

    private static bool MineAt(Int3 cell, bool[,,] mines) => TerrariumModel.IsInside(cell) && mines[cell.X, cell.Y, cell.Z];

    private static string DigitSegments(char digit) => digit switch
    {
        '0' => "abcdef", '1' => "bc", '2' => "abdeg", '3' => "abcdg", '4' => "bcfg",
        '5' => "acdfg", '6' => "acdefg", '7' => "abc", '8' => "abcdefg", '9' => "abcdfg",
        _ => string.Empty
    };

    private void AddPlaneBox(Vector3 center, Vector3 u, Vector3 v, Vector3 size, Color color)
    {
        var hu = u * size.X / 2f; var hv = v * size.Y / 2f;
        var hw = Vector3.Normalize(Vector3.Cross(u, v)) * size.Z / 2f;
        var b00 = center - hu - hv - hw; var b10 = center + hu - hv - hw;
        var b11 = center + hu + hv - hw; var b01 = center - hu + hv - hw;
        var t00 = b00 + hw * 2; var t10 = b10 + hw * 2; var t11 = b11 + hw * 2; var t01 = b01 + hw * 2;
        AddQuad(t00, t10, t11, t01, color, false);
        AddQuad(b01, b11, b10, b00, Color.Multiply(color, .48f), false);
        AddQuad(b10, b11, t11, t10, Color.Multiply(color, .72f), false);
        AddQuad(b01, b00, t00, t01, Color.Multiply(color, .58f), false);
        AddQuad(b00, b10, t10, t00, Color.Multiply(color, .57f), false);
        AddQuad(b11, b01, t01, t11, Color.Multiply(color, .68f), false);
    }

    private (Vector3 Center, Vector3 U, Vector3 V, Vector3 Normal) MoveHintGeometry(
        TerrariumModel model, Int3 target, bool excavation)
    {
        // XY destinations sit on the landing surface. Vertical destinations
        // occupy the center of their cell and face the selected movement plane.
        // Excavation hints are moved to the camera-facing cell face so the
        // depth buffer does not hide them inside the terrain being excavated.
        if (model.Plane == MovementPlane.XZ)
        {
            var normal = CameraFacingNormal(Vector3.UnitY, target.Y);
            var offset = excavation ? .505f : .012f;
            return (new Vector3(target.X, target.Y, target.Z + .49f) + normal * offset,
                Vector3.UnitX, Vector3.UnitZ, normal);
        }
        if (model.Plane == MovementPlane.YZ)
        {
            var normal = CameraFacingNormal(Vector3.UnitX, target.X);
            var offset = excavation ? .505f : .012f;
            return (new Vector3(target.X, target.Y, target.Z + .49f) + normal * offset,
                Vector3.UnitY, Vector3.UnitZ, normal);
        }

        var above = _cameraPosition.Z >= target.Z + .5f;
        var normalZ = above ? Vector3.UnitZ : -Vector3.UnitZ;
        var z = excavation ? (above ? target.Z + 1.025f : target.Z - .025f) : target.Z + (above ? .025f : -.025f);
        return (new Vector3(target.X, target.Y, z), Vector3.UnitX, above ? Vector3.UnitY : -Vector3.UnitY, normalZ);
    }

    private Vector3 CameraFacingNormal(Vector3 axis, float coordinate)
    {
        var cameraCoordinate = axis.X != 0 ? _cameraPosition.X : _cameraPosition.Y;
        return cameraCoordinate >= coordinate ? axis : -axis;
    }

    private void BuildPiece(RenderPiece rendered, bool selected)
    {
        var p = rendered.Piece;
        var opacity = rendered.Opacity;
        var transparent = opacity < .98f;
        var body = ApplyOpacity(p.Side == Side.White ? new Color(235, 225, 199) : new Color(35, 41, 55), opacity);
        var trim = ApplyOpacity(p.Side == Side.White ? new Color(73, 218, 200) : new Color(231, 83, 109), opacity);
        var dark = ApplyOpacity(p.Side == Side.White ? new Color(154, 158, 146) : new Color(12, 16, 24), opacity);
        var at = rendered.Position;

        if (selected)
        {
            AddDisc(at + new Vector3(0, 0, .015f), .48f, 24, ApplyOpacity(new Color(78, 255, 224, 115), opacity), true);
            AddRing(at + new Vector3(0, 0, .021f), .49f, .035f, 28, ApplyOpacity(new Color(119, 255, 233), opacity), true);
        }

        AddCylinder(at, .34f, .11f, 20, trim, transparent);
        AddCylinder(at + new Vector3(0, 0, .09f), .28f, .10f, 20, body, transparent);
        AddCone(at + new Vector3(0, 0, .18f), .22f, .42f, 20, dark, transparent);

        var crown = at + new Vector3(0, 0, .57f);
        switch (p.Kind)
        {
            case PieceKind.Pawn:
                AddSphere(crown, .16f, 10, 8, body, transparent);
                break;
            case PieceKind.Rook:
                AddCylinder(crown - new Vector3(0, 0, .03f), .22f, .26f, 12, body, transparent);
                for (var i = 0; i < 4; i++)
                {
                    var a = i * MathHelper.PiOver2;
                    AddBox(crown + new Vector3(MathF.Cos(a) * .15f, MathF.Sin(a) * .15f, .17f), new Vector3(.14f, .14f, .16f), trim, transparent);
                }
                break;
            case PieceKind.Knight:
                AddSphere(crown + new Vector3(0, 0, .02f), .19f, 9, 7, body, transparent);
                AddCone(crown + new Vector3(0, -.04f, .05f), .18f, .42f, 12, body, transparent, Matrix.CreateRotationX(-.72f));
                AddSphere(crown + new Vector3(0, -.24f, .31f), .12f, 9, 7, body, transparent);
                break;
            case PieceKind.Bishop:
                AddSphere(crown, .19f, 12, 8, body, transparent);
                AddCone(crown + new Vector3(0, 0, .16f), .12f, .30f, 12, trim, transparent);
                break;
            case PieceKind.Trishop:
                AddSphere(crown, .18f, 12, 8, body, transparent);
                for (var i = 0; i < 3; i++)
                {
                    var a = i * MathHelper.TwoPi / 3f;
                    AddCone(crown + new Vector3(MathF.Cos(a) * .12f, MathF.Sin(a) * .12f, .10f), .08f, .30f, 8, trim, transparent);
                }
                break;
            case PieceKind.Queen:
                AddCone(crown - new Vector3(0, 0, .02f), .25f, .28f, 16, body, transparent);
                for (var i = 0; i < 5; i++)
                {
                    var a = i * MathHelper.TwoPi / 5f;
                    AddSphere(crown + new Vector3(MathF.Cos(a) * .20f, MathF.Sin(a) * .20f, .27f), .065f, 7, 5, trim, transparent);
                }
                break;
            case PieceKind.King:
                AddSphere(crown, .18f, 12, 8, body, transparent);
                AddBox(crown + new Vector3(0, 0, .28f), new Vector3(.07f, .07f, .33f), trim, transparent);
                AddBox(crown + new Vector3(0, 0, .34f), new Vector3(.28f, .07f, .07f), trim, transparent);
                break;
        }
    }

    private enum Face { Top, Bottom, East, West, North, South }

    private void AddCellFace(int x, int y, int z, Face face, Color color, bool transparent)
    {
        var min = new Vector3(x - .49f, y - .49f, z);
        var max = new Vector3(x + .49f, y + .49f, z + .98f);
        switch (face)
        {
            case Face.Top: AddQuad(new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), color, transparent); break;
            case Face.Bottom: AddQuad(new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, min.Y, min.Z), new(min.X, min.Y, min.Z), color, transparent); break;
            case Face.East: AddQuad(new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(max.X, min.Y, max.Z), color, transparent); break;
            case Face.West: AddQuad(new(min.X, max.Y, min.Z), new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), color, transparent); break;
            case Face.North: AddQuad(new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z), color, transparent); break;
            case Face.South: AddQuad(new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z), new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z), color, transparent); break;
        }
    }

    private static bool Solid(bool[,,] solids, int x, int y, int z) =>
        x is >= 0 and < 8 && y is >= 0 and < 8 && z is >= 0 and < 16 && solids[x, y, z];

    private static Color TerrainColor(int x, int y, int z, float shade, float opacity)
    {
        // Including Z in the parity reverses the checkerboard on every layer.
        var baseColor = (x + y + z) % 2 == 0
            ? new Color(237, 214, 176)
            : new Color(184, 135, 98);
        return ApplyOpacity(Color.Multiply(baseColor, shade), opacity);
    }

    private static Color ApplyOpacity(Color color, float opacity) =>
        new(color.R, color.G, color.B, (byte)Math.Clamp((int)(color.A * opacity), 0, 255));

    private void AddCylinder(Vector3 center, float radius, float height, int segments, Color color, bool transparent)
    {
        var top = center.Z + height;
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathHelper.TwoPi / segments;
            var b = (i + 1) * MathHelper.TwoPi / segments;
            var p0 = center + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0);
            var p1 = center + new Vector3(MathF.Cos(b) * radius, MathF.Sin(b) * radius, 0);
            var p2 = new Vector3(p1.X, p1.Y, top);
            var p3 = new Vector3(p0.X, p0.Y, top);
            var shade = .68f + .25f * MathF.Max(0, MathF.Sin(a));
            AddQuad(p0, p1, p2, p3, Color.Multiply(color, shade), transparent);
            AddTriangle(new Vector3(center.X, center.Y, top), p3, p2, color, transparent);
        }
    }

    private void AddCone(Vector3 center, float radius, float height, int segments, Color color, bool transparent, Matrix? rotation = null)
    {
        var transform = rotation ?? Matrix.Identity;
        var tipLocal = new Vector3(0, 0, height);
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathHelper.TwoPi / segments;
            var b = (i + 1) * MathHelper.TwoPi / segments;
            var p0 = Vector3.Transform(new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0), transform) + center;
            var p1 = Vector3.Transform(new Vector3(MathF.Cos(b) * radius, MathF.Sin(b) * radius, 0), transform) + center;
            var tip = Vector3.Transform(tipLocal, transform) + center;
            AddTriangle(p0, p1, tip, Color.Multiply(color, .72f + .24f * MathF.Max(0, MathF.Sin(a))), transparent);
        }
    }

    private void AddSphere(Vector3 center, float radius, int longitude, int latitude, Color color, bool transparent)
    {
        for (var lat = 0; lat < latitude; lat++)
        {
            var p0 = -MathHelper.PiOver2 + lat * MathHelper.Pi / latitude;
            var p1 = -MathHelper.PiOver2 + (lat + 1) * MathHelper.Pi / latitude;
            for (var lon = 0; lon < longitude; lon++)
            {
                var a0 = lon * MathHelper.TwoPi / longitude;
                var a1 = (lon + 1) * MathHelper.TwoPi / longitude;
                Vector3 Point(float p, float a) => center + new Vector3(MathF.Cos(p) * MathF.Cos(a), MathF.Cos(p) * MathF.Sin(a), MathF.Sin(p)) * radius;
                AddQuad(Point(p0, a0), Point(p0, a1), Point(p1, a1), Point(p1, a0), Color.Multiply(color, .82f + .18f * MathF.Sin(p1)), transparent);
            }
        }
    }

    private void AddBox(Vector3 center, Vector3 size, Color color, bool transparent)
    {
        var min = center - size / 2f; var max = center + size / 2f;
        AddQuad(new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z), new(max.X, max.Y, max.Z), new(min.X, max.Y, max.Z), color, transparent);
        AddQuad(new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, min.Y, min.Z), new(min.X, min.Y, min.Z), Color.Multiply(color, .48f), transparent);
        AddQuad(new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(max.X, min.Y, max.Z), Color.Multiply(color, .72f), transparent);
        AddQuad(new(min.X, max.Y, min.Z), new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), Color.Multiply(color, .58f), transparent);
        AddQuad(new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z), new(max.X, min.Y, max.Z), new(min.X, min.Y, max.Z), Color.Multiply(color, .57f), transparent);
        AddQuad(new(max.X, max.Y, min.Z), new(min.X, max.Y, min.Z), new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z), Color.Multiply(color, .68f), transparent);
    }

    private void AddDisc(Vector3 center, float radius, int segments, Color color, bool transparent)
    {
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathHelper.TwoPi / segments; var b = (i + 1) * MathHelper.TwoPi / segments;
            AddTriangle(center, center + new Vector3(MathF.Cos(a) * radius, MathF.Sin(a) * radius, 0), center + new Vector3(MathF.Cos(b) * radius, MathF.Sin(b) * radius, 0), color, transparent);
        }
    }

    private void AddRing(Vector3 center, float radius, float width, int segments, Color color, bool transparent)
    {
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathHelper.TwoPi / segments; var b = (i + 1) * MathHelper.TwoPi / segments;
            Vector3 P(float angle, float r) => center + new Vector3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, 0);
            AddQuad(P(a, radius), P(b, radius), P(b, radius - width), P(a, radius - width), color, transparent);
        }
    }

    private void AddFrame(Vector3 center, Vector2 size, Color color, float width)
    {
        var x = size.X / 2; var y = size.Y / 2;
        AddQuad(new(center.X - x, center.Y - y, center.Z), new(center.X + x, center.Y - y, center.Z), new(center.X + x, center.Y - y + width, center.Z), new(center.X - x, center.Y - y + width, center.Z), color, true);
        AddQuad(new(center.X - x, center.Y + y - width, center.Z), new(center.X + x, center.Y + y - width, center.Z), new(center.X + x, center.Y + y, center.Z), new(center.X - x, center.Y + y, center.Z), color, true);
        AddQuad(new(center.X - x, center.Y - y, center.Z), new(center.X - x + width, center.Y - y, center.Z), new(center.X - x + width, center.Y + y, center.Z), new(center.X - x, center.Y + y, center.Z), color, true);
        AddQuad(new(center.X + x - width, center.Y - y, center.Z), new(center.X + x, center.Y - y, center.Z), new(center.X + x, center.Y + y, center.Z), new(center.X + x - width, center.Y + y, center.Z), color, true);
    }

    private void AddPlaneFrame(Vector3 center, Vector3 u, Vector3 v, float size, Color color, float width)
    {
        var half = size / 2f;
        AddQuad(center - u * half - v * half, center + u * half - v * half,
            center + u * half - v * (half - width), center - u * half - v * (half - width), color, true);
        AddQuad(center - u * half + v * (half - width), center + u * half + v * (half - width),
            center + u * half + v * half, center - u * half + v * half, color, true);
        AddQuad(center - u * half - v * half, center - u * (half - width) - v * half,
            center - u * (half - width) + v * half, center - u * half + v * half, color, true);
        AddQuad(center + u * (half - width) - v * half, center + u * half - v * half,
            center + u * half + v * half, center + u * (half - width) + v * half, color, true);
    }

    private void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, bool transparent)
    {
        AddTriangle(a, b, c, color, transparent); AddTriangle(a, c, d, color, transparent);
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color color, bool transparent)
    {
        var triangle = new Triangle(new(a, color), new(b, color), new(c, color), (a + b + c) / 3f);
        (transparent ? _transparent : _opaque).Add(triangle);
    }

    private void Flush(List<Triangle> triangles, BlendState blend, DepthStencilState depth, bool sort)
    {
        if (triangles.Count == 0) return;
        IEnumerable<Triangle> ordered = sort ? triangles.OrderByDescending(t => Vector3.DistanceSquared(t.Center, _cameraPosition)) : triangles;
        var vertices = new VertexPositionColor[triangles.Count * 3];
        var index = 0;
        foreach (var triangle in ordered)
        {
            vertices[index++] = triangle.A; vertices[index++] = triangle.B; vertices[index++] = triangle.C;
        }
        _graphics.BlendState = blend;
        _graphics.DepthStencilState = depth;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _graphics.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, triangles.Count);
        }
    }

    public void Dispose() => _effect.Dispose();
}
