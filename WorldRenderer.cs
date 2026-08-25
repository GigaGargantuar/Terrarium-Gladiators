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

    public float Yaw { get; set; }
    public float HeightOffset { get; set; }
    public float ElevationDegrees { get; set; } = 45f;
    public float CameraZ => _cameraPosition.Z;
    public bool LayerFocus { get; set; }
    public int FocusLayer { get; set; } = 8;

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

    public void Draw(TerrariumModel model, bool[,,]? terrainOverride, IReadOnlyList<RenderPiece> pieces, IReadOnlyList<Int3> legalMoves)
    {
        UpdateCamera();
        _opaque.Clear();
        _transparent.Clear();

        BuildTerrain(terrainOverride ?? model.Solids);
        BuildLayerGuide();
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
        var geometry = MoveHintGeometry(model, target, model.IsExcavationTarget(target));
        var projected = _worldViewport.Project(geometry.Center, _projection, _view, Matrix.Identity);
        return new Vector2(projected.X, projected.Y);
    }

    private void UpdateCamera()
    {
        _worldViewport = new Viewport(0, 0, 1050, 900);
        var target = new Vector3(3.5f, 3.5f, 7.4f + HeightOffset);
        const float horizontalDistance = 13f;
        var verticalDistance = horizontalDistance * MathF.Tan(MathHelper.ToRadians(ElevationDegrees));
        var horizontal = new Vector3(MathF.Sin(Yaw) * horizontalDistance,
            -MathF.Cos(Yaw) * horizontalDistance, 0);
        _cameraPosition = target + horizontal + Vector3.UnitZ * verticalDistance;
        _view = Matrix.CreateLookAt(_cameraPosition, target, Vector3.UnitZ);
        var height = 17.6f;
        var width = height * _worldViewport.AspectRatio;
        _projection = Matrix.CreateOrthographic(width, height, .1f, 80f);
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
            if (z == FocusLayer) AddSliceCube(solids, x, y, z);
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
        var z = FocusLayer + .012f;
        var color = new Color(65, 235, 213, 22);
        AddQuad(new Vector3(-.55f, -.55f, z), new Vector3(7.55f, -.55f, z),
            new Vector3(7.55f, 7.55f, z), new Vector3(-.55f, 7.55f, z), color, true);
        AddFrame(new Vector3(3.5f, 3.5f, z), new Vector2(8.18f), new Color(93, 255, 229, 155), .035f);
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

        var z = excavation ? target.Z + 1.025f : target.Z + .025f;
        return (new Vector3(target.X, target.Y, z), Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);
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
        AddQuad(new(max.X, min.Y, min.Z), new(max.X, max.Y, min.Z), new(max.X, max.Y, max.Z), new(max.X, min.Y, max.Z), Color.Multiply(color, .72f), transparent);
        AddQuad(new(min.X, max.Y, min.Z), new(min.X, min.Y, min.Z), new(min.X, min.Y, max.Z), new(min.X, max.Y, max.Z), Color.Multiply(color, .58f), transparent);
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
