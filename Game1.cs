using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace TerrariumGladiators;

public sealed class Game1 : Game
{
    private enum MatchMode { SameDevicePvp, PlayerVsBot, BotVsBot }

    private sealed class FallVisual
    {
        public required PieceFallEvent Fall { get; init; }
        public float Delay { get; init; }
        public float Duration { get; init; }
        public float Elapsed { get; set; }
        public float Progress => MathHelper.Clamp((Elapsed - Delay) / Duration, 0, 1);
        public bool Finished => Elapsed >= Delay + Duration;
    }

    private const int WindowWidth = 1440;
    private const int WindowHeight = 900;
    private const float BotMoveDelay = .45f;
    private readonly GraphicsDeviceManager _graphics;
    private readonly TerrariumModel _model = new();
    private readonly TerrariumBot _bot = new();
    private readonly List<FallVisual> _falls = new();
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Texture2D _pixel = null!;
    private Texture2D _cursor = null!;
    private RenderTarget2D _sceneTarget = null!;
    private WorldRenderer _world = null!;
    private MouseState _oldMouse;
    private KeyboardState _oldKeyboard;
    private bool _showHelp;
    private bool _depthFocus;
    private int _selectedDepth = 8;
    private bool _leftPressArmed;
    private bool _worldPress;
    private bool _cameraDragging;
    private Point _dragStart;
    private Point _dragLast;
    private bool[,,]? _preImpactTerrain;
    private List<ChessPiece>? _preImpactPieces;
    private float _transitionElapsed;
    private float _impactTime;
    private float _botDelayRemaining = BotMoveDelay;
    private bool _botMoveUnavailable;
    private Task<BotMove?>? _botSearch;
    private int _positionVersion;
    private int _botSearchVersion;
    private MatchMode _matchMode = MatchMode.PlayerVsBot;

    private readonly Rectangle[] _planeButtons =
    {
        new(1090, 238, 88, 44), new(1187, 238, 88, 44), new(1284, 238, 88, 44)
    };
    private readonly Rectangle[] _promotionButtons =
    {
        new(208, 476, 145, 62), new(371, 476, 145, 62),
        new(534, 476, 145, 62), new(697, 476, 145, 62)
    };
    private static readonly PieceKind[] PromotionKinds =
        [PieceKind.Queen, PieceKind.Rook, PieceKind.Bishop, PieceKind.Knight];
    private readonly Rectangle[] _modeButtons =
    {
        new(1090, 826, 86, 25), new(1183, 826, 96, 25), new(1286, 826, 86, 25)
    };
    private static readonly string[] ModeLabels = ["PVP", "PV BOT", "BOT V BOT"];

    private bool IsBotTurn => _matchMode == MatchMode.BotVsBot ||
                              _matchMode == MatchMode.PlayerVsBot && _model.Turn == Side.Black;
    private bool IsHumanTurn => !IsBotTurn;

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = WindowWidth,
            PreferredBackBufferHeight = WindowHeight,
            SynchronizeWithVerticalRetrace = true,
            PreferMultiSampling = true,
            IsFullScreen = true,
            HardwareModeSwitch = false
        };
        var displayMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
        _graphics.PreferredBackBufferWidth = displayMode.Width;
        _graphics.PreferredBackBufferHeight = displayMode.Height;
        Content.RootDirectory = "Content";
        // Draw the cursor into the backbuffer so game-capture software such as
        // OBS records it reliably. The operating-system cursor is an overlay
        // and may be omitted by game capture even when IsMouseVisible is true.
        IsMouseVisible = false;
        Window.Title = "Terrarium Gladiators";
        Window.AllowUserResizing = true;
    }

    protected override void Initialize()
    {
        _graphics.PreparingDeviceSettings += (_, args) => args.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 4;
        _graphics.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("UIFont");
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        _cursor = CreateCursorTexture();
        _sceneTarget = new RenderTarget2D(GraphicsDevice, WindowWidth, WindowHeight, false,
            SurfaceFormat.Color, DepthFormat.Depth24, 4, RenderTargetUsage.DiscardContents);
        _world = new WorldRenderer(GraphicsDevice);
    }

    protected override void UnloadContent()
    {
        _world.Dispose();
        _sceneTarget.Dispose();
        _cursor.Dispose();
        _pixel.Dispose();
        _spriteBatch.Dispose();
        base.UnloadContent();
    }

    protected override void Update(GameTime gameTime)
    {
        var elapsed = (float)gameTime.ElapsedGameTime.TotalSeconds;
        foreach (var fall in _falls) fall.Elapsed += elapsed;
        _falls.RemoveAll(fall => fall.Finished);
        if (_preImpactTerrain is not null)
        {
            _transitionElapsed += elapsed;
            if (_transitionElapsed >= _impactTime)
            {
                _preImpactTerrain = null;
                _preImpactPieces = null;
            }
        }

        var keyboard = Keyboard.GetState();
        var mouse = Mouse.GetState();
        if (Pressed(keyboard, Keys.Escape))
        {
            if (_showHelp) _showHelp = false;
            else Exit();
        }
        if (Pressed(keyboard, Keys.H)) _showHelp = !_showHelp;
        var promotionReady = _model.PendingPromotionPieceId is not null && IsHumanTurn && _preImpactTerrain is null;
        if (promotionReady)
        {
            if (Pressed(keyboard, Keys.Q)) CompletePromotion(PieceKind.Queen);
            else if (Pressed(keyboard, Keys.R)) CompletePromotion(PieceKind.Rook);
            else if (Pressed(keyboard, Keys.B)) CompletePromotion(PieceKind.Bishop);
            else if (Pressed(keyboard, Keys.N)) CompletePromotion(PieceKind.Knight);
        }
        if (!promotionReady && Pressed(keyboard, Keys.R)) ResetGame();
        if (Pressed(keyboard, Keys.U)) Undo();
        if (IsHumanTurn && (Pressed(keyboard, Keys.D1) || Pressed(keyboard, Keys.NumPad1)))
            _model.SetPlane(MovementPlane.XY);
        if (IsHumanTurn && (Pressed(keyboard, Keys.D2) || Pressed(keyboard, Keys.NumPad2)))
            _model.SetPlane(MovementPlane.XZ);
        if (IsHumanTurn && (Pressed(keyboard, Keys.D3) || Pressed(keyboard, Keys.NumPad3)))
            _model.SetPlane(MovementPlane.YZ);
        if (IsHumanTurn && Pressed(keyboard, Keys.Space))
            _model.SetPlane((MovementPlane)(((int)_model.Plane + 1) % 3));
        if (!promotionReady && Pressed(keyboard, Keys.Q)) _world.Yaw -= MathHelper.PiOver2;
        if (Pressed(keyboard, Keys.E)) _world.Yaw += MathHelper.PiOver2;
        if (keyboard.IsKeyDown(Keys.Up))
            _world.HeightOffset = MathHelper.Clamp(_world.HeightOffset + elapsed * 4f, -7f, 7f);
        if (keyboard.IsKeyDown(Keys.Down))
            _world.HeightOffset = MathHelper.Clamp(_world.HeightOffset - elapsed * 4f, -7f, 7f);
        if (IsHumanTurn && Pressed(keyboard, Keys.Tab))
            SelectNextPiece(keyboard.IsKeyDown(Keys.LeftShift) || keyboard.IsKeyDown(Keys.RightShift));

        if (mouse.MiddleButton == ButtonState.Pressed && _oldMouse.MiddleButton == ButtonState.Released)
            _depthFocus = !_depthFocus;
        var wheel = mouse.ScrollWheelValue - _oldMouse.ScrollWheelValue;
        if (_depthFocus && wheel != 0) _selectedDepth = Math.Clamp(_selectedDepth + Math.Sign(wheel), 0, 15);
        _world.LayerFocus = _depthFocus;
        _world.FocusLayer = _selectedDepth;

        var mousePoint = ToVirtualPoint(mouse.Position);
        if (mouse.LeftButton == ButtonState.Pressed && _oldMouse.LeftButton == ButtonState.Released)
        {
            _leftPressArmed = true;
            _worldPress = mousePoint.X is >= 0 and < 1050 && mousePoint.Y is >= 0 and < WindowHeight;
            _cameraDragging = false;
            _dragStart = mousePoint;
            _dragLast = mousePoint;
        }
        if (mouse.LeftButton == ButtonState.Pressed && _leftPressArmed && _worldPress && mousePoint.X >= 0)
        {
            var total = mousePoint - _dragStart;
            if (total.X * total.X + total.Y * total.Y > 20) _cameraDragging = true;
            if (_cameraDragging)
            {
                var delta = mousePoint - _dragLast;
                _world.Yaw -= delta.X * .009f;
                _world.ElevationDegrees = MathHelper.Clamp(_world.ElevationDegrees + delta.Y * .18f, 30f, 60f);
            }
            _dragLast = mousePoint;
        }
        if (mouse.LeftButton == ButtonState.Released && _oldMouse.LeftButton == ButtonState.Pressed)
        {
            if (_leftPressArmed && !_cameraDragging && !_showHelp && _preImpactTerrain is null)
                HandleClick(mousePoint);
            _leftPressArmed = false;
            _cameraDragging = false;
        }
        if (mouse.RightButton == ButtonState.Pressed && _oldMouse.RightButton == ButtonState.Released)
            _model.ClearSelection();

        UpdateBot(elapsed);

        _oldKeyboard = keyboard;
        _oldMouse = mouse;
        base.Update(gameTime);
    }

    private bool Pressed(KeyboardState current, Keys key) => current.IsKeyDown(key) && !_oldKeyboard.IsKeyDown(key);

    private Point ToVirtualPoint(Point screenPoint)
    {
        var bounds = AspectFitRectangle(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(screenPoint)) return new Point(-10_000, -10_000);
        var x = (screenPoint.X - bounds.X) * WindowWidth / (float)bounds.Width;
        var y = (screenPoint.Y - bounds.Y) * WindowHeight / (float)bounds.Height;
        return new Point((int)x, (int)y);
    }

    private static Rectangle AspectFitRectangle(int width, int height)
    {
        if (width <= 0 || height <= 0) return Rectangle.Empty;
        var scale = Math.Min(width / (float)WindowWidth, height / (float)WindowHeight);
        var fittedWidth = Math.Max(1, (int)(WindowWidth * scale));
        var fittedHeight = Math.Max(1, (int)(WindowHeight * scale));
        return new Rectangle((width - fittedWidth) / 2, (height - fittedHeight) / 2, fittedWidth, fittedHeight);
    }

    private void ResetGame()
    {
        _model.Reset();
        _positionVersion++;
        _botDelayRemaining = BotMoveDelay;
        _botMoveUnavailable = false;
        ClearTransition();
    }

    private void SetMatchMode(MatchMode mode)
    {
        if (_matchMode == mode) return;
        _matchMode = mode;
        ResetGame();
    }

    private void Undo()
    {
        if (!_model.Undo()) return;

        // From the player's turn, undo both the bot reply and the player's
        // preceding move. During the bot's delay, a single undo is sufficient.
        if (_matchMode == MatchMode.PlayerVsBot && _model.Turn == Side.Black &&
            _model.Winner is null) _model.Undo();
        _positionVersion++;
        _botDelayRemaining = BotMoveDelay;
        _botMoveUnavailable = false;
        ClearTransition();
    }

    private void ClearTransition()
    {
        _falls.Clear();
        _preImpactTerrain = null;
        _preImpactPieces = null;
    }

    private void CompletePromotion(PieceKind kind)
    {
        if (!_model.Promote(kind)) return;
        _positionVersion++;
        _botDelayRemaining = BotMoveDelay;
        _botMoveUnavailable = false;
    }

    private void UpdateBot(float elapsed)
    {
        if (_botSearch is not null)
        {
            if (!_botSearch.IsCompleted) return;

            BotMove? completedMove;
            try
            {
                completedMove = _botSearch.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                _botMoveUnavailable = true;
                _model.SetMessage($"Bot search failed: {exception.Message}");
                _botSearch = null;
                return;
            }
            _botSearch = null;

            // Reset/undo may have replaced the position while the background
            // search was running. Never apply a result to a different state.
            if (_botSearchVersion != _positionVersion || !IsBotTurn ||
                _model.Winner is not null)
            {
                _botDelayRemaining = BotMoveDelay;
                return;
            }

            if (completedMove is null)
            {
                _botMoveUnavailable = true;
                _model.SetMessage($"{_model.Turn} bot has no legal move.");
                return;
            }

            ExecuteBotMove(completedMove.Value);
            return;
        }

        if (_model.Winner is not null || !IsBotTurn)
        {
            _botDelayRemaining = BotMoveDelay;
            _botMoveUnavailable = false;
            return;
        }
        if (_showHelp || _preImpactTerrain is not null || _botMoveUnavailable) return;

        _botDelayRemaining -= elapsed;
        if (_botDelayRemaining > 0) return;
        var searchPosition = _model.CloneForSimulation();
        _botSearchVersion = _positionVersion;
        _botSearch = Task.Run(() => _bot.ChooseMove(searchPosition));
    }

    private void ExecuteBotMove(BotMove move)
    {
        var moving = _model.Pieces.First(piece => piece.Id == move.PieceId);
        var from = moving.Position;
        var terrainBefore = (bool[,,])_model.Solids.Clone();
        var piecesBefore = _model.Pieces.Select(piece => piece.Clone()).ToList();
        _model.SetPlane(move.Plane);
        _model.Select(move.PieceId);
        if (_model.TryMove(move.Target))
        {
            if (_model.PendingPromotionPieceId is not null) _model.Promote(PieceKind.Queen);
            _positionVersion++;
            StartTransition(move.PieceId, from, terrainBefore, piecesBefore);
        }
        _botDelayRemaining = BotMoveDelay;
    }

    private void SelectNextPiece(bool backwards)
    {
        var own = _model.Pieces.Where(p => p.Side == _model.Turn).OrderBy(p => p.Id).ToList();
        if (own.Count == 0) return;
        var current = own.FindIndex(p => p.Id == _model.SelectedId);
        current = backwards ? (current <= 0 ? own.Count - 1 : current - 1) : (current + 1) % own.Count;
        _model.Select(own[current].Id);
    }

    private void HandleClick(Point point)
    {
        for (var i = 0; i < _modeButtons.Length; i++)
        {
            if (!_modeButtons[i].Contains(point)) continue;
            SetMatchMode((MatchMode)i);
            return;
        }
        if (_model.PendingPromotionPieceId is not null && _preImpactTerrain is null)
        {
            for (var i = 0; i < _promotionButtons.Length; i++)
            {
                if (!_promotionButtons[i].Contains(point)) continue;
                CompletePromotion(PromotionKinds[i]);
                return;
            }
            return;
        }
        if (new Rectangle(1090, 774, 135, 43).Contains(point)) { Undo(); return; }
        if (new Rectangle(1237, 774, 135, 43).Contains(point)) { ResetGame(); return; }
        if (new Rectangle(1330, 26, 42, 34).Contains(point)) { _showHelp = true; return; }

        for (var i = 0; i < _planeButtons.Length; i++)
        {
            if (!_planeButtons[i].Contains(point)) continue;
            if (IsHumanTurn) _model.SetPlane((MovementPlane)i);
            return;
        }
        if (!IsHumanTurn) return;
        if (point.X >= 1050) return;

        if (_model.Selected is not null)
        {
            var target = _model.LegalMoves()
                .Select(move => (Move: move, Distance: Vector2.Distance(point.ToVector2(), _world.ProjectTarget(_model, move))))
                .Where(candidate => candidate.Distance < 30f)
                .OrderBy(candidate => candidate.Distance)
                .FirstOrDefault();
            if (target.Distance > 0 && target.Distance < 30f)
            {
                var moving = _model.Selected!;
                var movingId = moving.Id;
                var from = moving.Position;
                var terrainBefore = (bool[,,])_model.Solids.Clone();
                var piecesBefore = _model.Pieces.Select(piece => piece.Clone()).ToList();
                if (_model.TryMove(target.Move))
                {
                    _positionVersion++;
                    StartTransition(movingId, from, terrainBefore, piecesBefore);
                }
                return;
            }
        }

        var clicked = _model.Pieces
            .Select(piece =>
            {
                var screen = _world.Project(new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z + .48f));
                return (Piece: piece, Distance: Vector2.Distance(point.ToVector2(), screen), Depth: screen.Y);
            })
            .Where(candidate => candidate.Distance < 32f)
            .OrderBy(candidate => candidate.Distance).ThenByDescending(candidate => candidate.Depth).FirstOrDefault();
        if (clicked.Piece is not null) _model.Select(clicked.Piece.Id);
        else _model.ClearSelection();
    }

    private void StartTransition(int movingId, Int3 moveFrom, bool[,,] terrainBefore, List<ChessPiece> piecesBefore)
    {
        _falls.Clear();
        _preImpactTerrain = terrainBefore;
        _preImpactPieces = piecesBefore;
        _transitionElapsed = 0;
        var delays = new Dictionary<int, float>();

        var firstFall = _model.LastFalls.FirstOrDefault(fall => fall.PieceId == movingId);
        var survivingMover = _model.Pieces.FirstOrDefault(piece => piece.Id == movingId);
        var moveTo = firstFall?.From ?? survivingMover?.Position ?? moveFrom;
        var moveDuration = 0f;
        if (moveFrom != moveTo)
        {
            var travel = Math.Abs(moveTo.X - moveFrom.X) + Math.Abs(moveTo.Y - moveFrom.Y) + Math.Abs(moveTo.Z - moveFrom.Z);
            moveDuration = .20f + travel * .045f;
            _falls.Add(new FallVisual
            {
                Fall = new PieceFallEvent(movingId, piecesBefore.First(p => p.Id == movingId).Side,
                    piecesBefore.First(p => p.Id == movingId).Kind, moveFrom, moveTo, false),
                Duration = moveDuration
            });
            delays[movingId] = moveDuration;
        }
        foreach (var fall in _model.LastFalls)
        {
            var distance = Math.Max(1, Math.Abs(fall.From.X - fall.To.X) +
                Math.Abs(fall.From.Y - fall.To.Y) + Math.Abs(fall.From.Z - fall.To.Z));
            var delay = fall.StartsWithMove ? delays.GetValueOrDefault(fall.PieceId)
                : Math.Max(moveDuration, delays.GetValueOrDefault(fall.PieceId));
            var duration = fall.StartsWithMove ? .20f + distance * .045f : .24f + distance * .07f;
            _falls.Add(new FallVisual { Fall = fall, Delay = delay, Duration = duration });
            delays[fall.PieceId] = delay + duration;
        }
        _impactTime = Math.Max(.08f, _falls.Count == 0 ? .08f : _falls.Max(fall => fall.Delay + fall.Duration));
    }

    private FallVisual? ActiveFall(int pieceId)
    {
        var candidates = _falls.Where(fall => fall.Fall.PieceId == pieceId).OrderBy(fall => fall.Delay).ToList();
        return candidates.LastOrDefault(fall => fall.Elapsed >= fall.Delay) ?? candidates.FirstOrDefault();
    }

    private static Vector3 AnimatedPosition(FallVisual animation)
    {
        var t = animation.Progress;
        var from = new Vector3(animation.Fall.From.X, animation.Fall.From.Y, animation.Fall.From.Z);
        var to = new Vector3(animation.Fall.To.X, animation.Fall.To.Y, animation.Fall.To.Z);
        var position = Vector3.Lerp(from, to, t * t);
        if (!animation.Fall.Perished && t > .78f)
            position.Z += MathF.Sin((t - .78f) / .22f * MathF.PI) * .16f;
        return position;
    }

    private List<RenderPiece> RenderPieces()
    {
        var result = new List<RenderPiece>();
        foreach (var piece in _model.Pieces)
        {
            var animation = ActiveFall(piece.Id);
            var position = animation is null ? new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z) : AnimatedPosition(animation);
            var visualPiece = piece;
            var before = _preImpactPieces?.FirstOrDefault(candidate => candidate.Id == piece.Id);
            if (before is not null && before.Kind != piece.Kind) visualPiece = before;
            result.Add(new RenderPiece(visualPiece, position, 1f));
        }
        foreach (var animation in _falls.Where(fall => fall.Fall.Perished && ActiveFall(fall.Fall.PieceId) == fall))
        {
            var ghost = new ChessPiece { Id = animation.Fall.PieceId, Side = animation.Fall.Side, Kind = animation.Fall.Kind, Position = animation.Fall.To };
            result.Add(new RenderPiece(ghost, AnimatedPosition(animation), 1f - MathF.Pow(animation.Progress, 3)));
        }
        if (_preImpactPieces is not null)
        {
            var finalIds = _model.Pieces.Select(piece => piece.Id).ToHashSet();
            var animatedIds = _falls.Select(fall => fall.Fall.PieceId).ToHashSet();
            foreach (var piece in _preImpactPieces.Where(piece => !finalIds.Contains(piece.Id) && !animatedIds.Contains(piece.Id)))
                result.Add(new RenderPiece(piece, new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z), 1f));
        }
        return result;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.SetRenderTarget(_sceneTarget);
        GraphicsDevice.Viewport = new Viewport(0, 0, WindowWidth, WindowHeight);
        GraphicsDevice.Clear(new Color(7, 11, 20));
        DrawBackdrop();
        var legalMoves = _preImpactTerrain is null ? _model.LegalMoves() : Array.Empty<Int3>();
        _world.Draw(_model, _preImpactTerrain, RenderPieces(), legalMoves);
        DrawInterface();

        GraphicsDevice.SetRenderTarget(null);
        GraphicsDevice.Clear(new Color(3, 6, 12));
        var destination = AspectFitRectangle(GraphicsDevice.PresentationParameters.BackBufferWidth,
            GraphicsDevice.PresentationParameters.BackBufferHeight);
        _spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
        _spriteBatch.Draw(_sceneTarget, destination, Color.White);
        _spriteBatch.End();
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawSoftwareCursor(Mouse.GetState().Position);
        _spriteBatch.End();
        base.Draw(gameTime);
    }

    private Texture2D CreateCursorTexture()
    {
        string[] pixels =
        [
            "X...................",
            "XX..................",
            "XOX.................",
            "XOOX................",
            "XOOOX...............",
            "XOOOOX..............",
            "XOOOOOX.............",
            "XOOOOOOX............",
            "XOOOOOOOX...........",
            "XOOOOOOOOX..........",
            "XOOOOOOOOOX.........",
            "XOOOOOOOOOOX........",
            "XOOOOOOXXXXXX.......",
            "XOOOXOOX............",
            "XOOX.XOOX...........",
            "XXX..XOOX...........",
            "XX....XOOX..........",
            "......XOOX..........",
            ".......XX..........."
        ];
        var texture = new Texture2D(GraphicsDevice, pixels[0].Length, pixels.Length);
        var colors = new Color[texture.Width * texture.Height];
        for (var y = 0; y < texture.Height; y++)
        for (var x = 0; x < texture.Width; x++)
            colors[y * texture.Width + x] = pixels[y][x] switch
            {
                'X' => new Color(3, 8, 14),
                'O' => new Color(238, 226, 200),
                _ => Color.Transparent
            };
        texture.SetData(colors);
        return texture;
    }

    private void DrawSoftwareCursor(Point position)
    {
        var width = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var height = GraphicsDevice.PresentationParameters.BackBufferHeight;
        if (!IsActive || position.X < 0 || position.Y < 0 || position.X >= width || position.Y >= height) return;

        // Keep the pointer a consistent physical size at any fullscreen or
        // resized-window resolution. PointClamp preserves its crisp outline.
        _spriteBatch.Draw(_cursor, position.ToVector2(), null, Color.White, 0f,
            Vector2.Zero, 1.35f, SpriteEffects.None, 0f);
    }

    private void DrawBackdrop()
    {
        _spriteBatch.Begin();
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1050, 900), new Color(8, 13, 24));
        for (var i = 0; i < 22; i++)
        {
            var x = 24 + i * 193 % 990; var y = 19 + i * 107 % 845;
            _spriteBatch.Draw(_pixel, new Rectangle(x, y, i % 4 == 0 ? 2 : 1, i % 4 == 0 ? 2 : 1), new Color(67, 222, 214, 72));
        }
        _spriteBatch.Draw(_pixel, new Rectangle(1050, 0, 390, 900), new Color(13, 19, 31));
        _spriteBatch.Draw(_pixel, new Rectangle(1050, 0, 2, 900), new Color(45, 211, 198, 90));
        _spriteBatch.End();
    }

    private void DrawInterface()
    {
        _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        DrawWorldLabels();
        DrawText("TERRARIUM", new Vector2(1090, 27), new Color(82, 241, 216), .88f);
        DrawText("GLADIATORS", new Vector2(1090, 58), new Color(236, 226, 200), .88f);
        DrawText($"TRUE 3D / {_world.ElevationDegrees:00}° / CAM Z {_world.CameraZ:00.0}", new Vector2(1092, 94), new Color(110, 145, 151), .43f);
        DrawText("?", new Vector2(1343, 31), new Color(113, 220, 209), .82f);

        var sideColor = _model.Turn == Side.White ? new Color(238, 224, 191) : new Color(239, 98, 118);
        var headline = _model.Winner is not null
            ? $"{_model.Winner.Value.ToString().ToUpper()} WINS"
            : IsBotTurn ? $"{_model.Turn.ToString().ToUpper()} BOT THINKING" : $"{_model.Turn.ToString().ToUpper()} TO MOVE";
        DrawText(headline, new Vector2(1090, 148), sideColor, .78f);
        DrawText(_model.Selected is null ? "No piece selected" : $"{_model.Selected.Kind}  ·  {TerrariumModel.CellName(_model.Selected.Position)}", new Vector2(1090, 183), new Color(139, 170, 173), .55f);

        DrawText("MOVEMENT PLANE", new Vector2(1090, 211), new Color(104, 137, 145), .48f);
        for (var i = 0; i < 3; i++) DrawButton(_planeButtons[i], ((MovementPlane)i).ToString(), (int)_model.Plane == i, i + 1);
        DrawText("TEAL safe   ·   YELLOW crater/dig   ·   RED fatal", new Vector2(1090, 291), new Color(111, 145, 149), .39f);

        DrawText("FIELD REPORT", new Vector2(1090, 314), new Color(104, 137, 145), .48f);
        DrawPanel(new Rectangle(1090, 341, 282, 114));
        DrawWrapped(_model.Message, new Rectangle(1105, 355, 252, 86), new Color(206, 214, 202), .53f, 19);

        DrawText("ARENA PROTOCOL", new Vector2(1090, 489), new Color(104, 137, 145), .48f);
        DrawRule(new Vector2(1090, 523), new Color(83, 241, 211), "0–1 CELL DROP", "Safe landing · pieces may stack");
        DrawRule(new Vector2(1090, 581), new Color(255, 99, 92), "2+ CELL DROP", "Impact crushes terrain or piece");
        DrawRule(new Vector2(1090, 639), new Color(242, 179, 87), "4+ / TERRAIN", "Fatal unless a piece breaks the fall");

        DrawText("Drag X: orbit   ·   Drag Y: 30°–60°", new Vector2(1090, 696), new Color(119, 150, 155), .40f);
        DrawText(_depthFocus ? $"MB  LAYER FOCUS: Z{_selectedDepth:00}" : "MB  LAYER FOCUS: OFF", new Vector2(1090, 720), _depthFocus ? new Color(82, 241, 216) : new Color(119, 150, 155), .48f);
        DrawText("Wheel  select depth   ·   H  rules", new Vector2(1090, 744), new Color(119, 150, 155), .43f);
        DrawSmallButton(new Rectangle(1090, 774, 135, 43), "U  UNDO");
        DrawSmallButton(new Rectangle(1237, 774, 135, 43), "R  RESTART");
        for (var i = 0; i < _modeButtons.Length; i++)
            DrawModeButton(_modeButtons[i], ModeLabels[i], (int)_matchMode == i);
        DrawText("Right-click to deselect  ·  Esc to quit", new Vector2(1090, 861), new Color(74, 104, 111), .43f);

        if (_showHelp) DrawHelpOverlay();
        else if (_model.PendingPromotionPieceId is not null && _preImpactTerrain is null) DrawPromotionOverlay();
        _spriteBatch.End();
    }

    private void DrawPromotionOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, 1050, 900), new Color(2, 5, 11, 205));
        var panel = new Rectangle(155, 314, 742, 292);
        _spriteBatch.Draw(_pixel, panel, new Color(16, 25, 39));
        Border(panel, new Color(242, 179, 87), 2);
        DrawText("CHOOSE YOUR PROMOTION", new Vector2(246, 351), new Color(242, 179, 87), 1.02f);
        DrawText("The pawn reached the far rank. Select its new piece.", new Vector2(270, 401), new Color(169, 190, 188), .58f);
        for (var i = 0; i < _promotionButtons.Length; i++)
        {
            var rect = _promotionButtons[i];
            _spriteBatch.Draw(_pixel, rect, new Color(28, 49, 59));
            Border(rect, new Color(77, 208, 190), 1);
            var key = PromotionKinds[i] == PieceKind.Knight ? "N" : PromotionKinds[i].ToString()[0].ToString();
            DrawText(PromotionKinds[i].ToString().ToUpper(), new Vector2(rect.X + 18, rect.Y + 15), new Color(235, 225, 199), .53f);
            DrawText(key, new Vector2(rect.Right - 24, rect.Y + 6), new Color(242, 179, 87), .38f);
        }
        DrawText("Keyboard: Q  R  B  N", new Vector2(400, 561), new Color(112, 150, 153), .48f);
    }

    private void DrawWorldLabels()
    {
        if (_depthFocus)
        {
            var at = _world.Project(new Vector3(7.7f, 7.7f, _selectedDepth + .05f));
            DrawText($"Z{_selectedDepth:00}", at, new Color(94, 255, 229), .55f);
        }
    }

    private void DrawHelpOverlay()
    {
        _spriteBatch.Draw(_pixel, new Rectangle(0, 0, WindowWidth, WindowHeight), new Color(2, 5, 11, 225));
        var panel = new Rectangle(290, 120, 860, 660);
        _spriteBatch.Draw(_pixel, panel, new Color(16, 25, 39)); Border(panel, new Color(72, 224, 207), 2);
        DrawText("THE TERRARIUM IN TRUE 3D", new Vector2(350, 171), new Color(82, 241, 216), 1.05f);
        DrawText("The orthographic camera starts at 45° with adjustable orbit, elevation, and Z.", new Vector2(350, 213), new Color(175, 192, 188), .56f);
        DrawHelpStep(1, 350, 283, "SELECT", "Click a sculpted piece, then choose a highlighted 3D destination.");
        DrawHelpStep(2, 350, 365, "MOVE THE CAMERA", "Drag X to orbit, drag Y for 30°–60°, and use Up / Down for world Z.");
        DrawHelpStep(3, 350, 447, "ISOLATE DEPTH", "Middle-click, then scroll Z0–Z15. Other layer shells become transparent.");
        DrawHelpStep(4, 350, 529, "CLIMB OR EXCAVATE", "Vertical pawns need walls: hop, climb/capture on YZ, or dig straight up.");
        DrawHelpStep(5, 350, 611, "USE GRAVITY", "Falling pieces accelerate, bounce, crush, or fade on fatal impact.");
        DrawText("Press H / Esc to return", new Vector2(350, 720), new Color(233, 222, 194), .58f);
    }

    private void DrawHelpStep(int number, int x, int y, string title, string body)
    {
        var badge = new Rectangle(x, y, 46, 46); _spriteBatch.Draw(_pixel, badge, new Color(31, 69, 74));
        DrawText(number.ToString("00"), new Vector2(x + 10, y + 10), new Color(76, 233, 214), .62f);
        DrawText(title, new Vector2(x + 67, y - 1), new Color(236, 224, 195), .66f);
        DrawText(body, new Vector2(x + 67, y + 27), new Color(143, 165, 166), .49f);
    }

    private void DrawButton(Rectangle rect, string label, bool active, int key)
    {
        _spriteBatch.Draw(_pixel, rect, active ? new Color(37, 100, 99) : new Color(25, 35, 49));
        Border(rect, active ? new Color(77, 239, 215) : new Color(52, 72, 83), active ? 2 : 1);
        DrawText(label, new Vector2(rect.X + 15, rect.Y + 12), active ? new Color(239, 231, 201) : new Color(134, 159, 164), .58f);
        DrawText(key.ToString(), new Vector2(rect.Right - 18, rect.Y + 5), new Color(96, 129, 135), .36f);
    }

    private void DrawSmallButton(Rectangle rect, string label)
    {
        _spriteBatch.Draw(_pixel, rect, new Color(24, 37, 51)); Border(rect, new Color(56, 86, 94), 1);
        DrawText(label, new Vector2(rect.X + 18, rect.Y + 12), new Color(179, 197, 194), .52f);
    }

    private void DrawModeButton(Rectangle rect, string label, bool active)
    {
        _spriteBatch.Draw(_pixel, rect, active ? new Color(37, 100, 99) : new Color(24, 37, 51));
        Border(rect, active ? new Color(77, 239, 215) : new Color(56, 86, 94), active ? 2 : 1);
        var width = _font.MeasureString(label).X * .35f;
        DrawText(label, new Vector2(rect.Center.X - width / 2, rect.Y + 7),
            active ? new Color(239, 231, 201) : new Color(134, 159, 164), .35f);
    }

    private void DrawPanel(Rectangle rect) { _spriteBatch.Draw(_pixel, rect, new Color(9, 15, 25)); Border(rect, new Color(42, 72, 80), 1); }

    private void DrawRule(Vector2 at, Color color, string title, string body)
    {
        _spriteBatch.Draw(_pixel, new Rectangle((int)at.X, (int)at.Y + 3, 4, 39), color);
        DrawText(title, at + new Vector2(16, 0), color, .53f); DrawText(body, at + new Vector2(16, 23), new Color(132, 157, 159), .46f);
    }

    private void DrawWrapped(string text, Rectangle area, Color color, float scale, int lineHeight)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries); var line = string.Empty; var y = area.Y;
        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (_font.MeasureString(candidate).X * scale > area.Width && line.Length > 0)
            {
                DrawText(line, new Vector2(area.X, y), color, scale); y += lineHeight; line = word;
                if (y + lineHeight > area.Bottom) break;
            }
            else line = candidate;
        }
        if (line.Length > 0 && y + lineHeight <= area.Bottom) DrawText(line, new Vector2(area.X, y), color, scale);
    }

    private void DrawText(string text, Vector2 position, Color color, float scale) =>
        _spriteBatch.DrawString(_font, text, position, color, 0, Vector2.Zero, scale, SpriteEffects.None, 0);

    private void Border(Rectangle rect, Color color, int thickness)
    {
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
        _spriteBatch.Draw(_pixel, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
    }
}
