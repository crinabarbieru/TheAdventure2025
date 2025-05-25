using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using Silk.NET.SDL;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;
using System.Diagnostics;


namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private TextureData _gameOverTexture;
    private int _gameOverTextureId;
    private bool _isGameOver = false;

    private int _treatsCollected;
    private const int _treatsToWin = 5;

    private int _livesLetf;
    private const int _livesMax = 3;
    private bool _gameWon = false;
    private int _gameWonTextureId;
    private TextureData _gameWonTexture;

    private BitmapFontRenderer _counterRenderer;
    private SpriteSheet _counterSprite;

    private Level _currentLevel = new();
    private PlayerObject? _player;
    private PlayerObject? _playerCat;

    private const string _soundPath = "Assets/boop.wav";
    private Process _audioProcess;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }
    public GameRenderer GetRenderer() => _renderer;

    private const int ScreenWidth = 640;
    private const int ScreenHeight = 400;

    public void SetupWorld()
    {
        var playerSprite = SpriteSheet.Load(_renderer, "Player.json", "Assets");
        var playerCatSprite = SpriteSheet.Load(_renderer, "Cat.json", "Assets");

        _player = new(playerSprite, 100, 100, KeyBindings.ArrowKeys);
        _playerCat = new(playerCatSprite, 200, 200, KeyBindings.WASDKeys);

        _audioProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "afplay",
                Arguments = $"\"{_soundPath}\"",
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        if (level == null)
        {
            throw new Exception("Failed to load level");
        }

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
            {
                throw new Exception("Failed to load tile set");
            }

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
        {
            throw new Exception("Invalid level dimensions");
        }

        if (level.TileWidth == null || level.TileHeight == null)
        {
            throw new Exception("Invalid tile dimensions");
        }

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _gameOverTextureId = _renderer.LoadTexture(Path.Combine("Assets", "game_over.png"), out _gameOverTexture);
        _gameWonTextureId = _renderer.LoadTexture(Path.Combine("Assets", "game_won.png"), out _gameWonTexture);

        _treatsCollected = 0;
        _livesLetf = _livesMax;
        _counterSprite = SpriteSheet.Load(_renderer, "font.json", "Assets");
        _counterRenderer = new BitmapFontRenderer(_renderer, _counterSprite);
        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null || _playerCat == null)
        {
            return;
        }

        double p1Up = _input.IsUpPressed() ? 1.0 : 0.0;
        double p1Down = _input.IsDownPressed() ? 1.0 : 0.0;
        double p1Left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double p1Right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool p1IsAttacking = _input.IsKeySpacePressed() && (p1Up + p1Down + p1Left + p1Right <= 1);
        bool p1AddBomb = _input.IsKeyBPressed();

        double p2Up = _input.IsKeyWPressed() ? 1.0 : 0.0;
        double p2Down = _input.IsKeySPressed() ? 1.0 : 0.0;
        double p2Left = _input.IsKeyAPressed() ? 1.0 : 0.0;
        double p2Right = _input.IsKeyDPressed() ? 1.0 : 0.0;


        _player.UpdatePosition(p1Up, p1Down, p1Left, p1Right, 960, 640, msSinceLastFrame);
        _playerCat.UpdatePosition(p2Up, p2Down, p2Left, p2Right, 960, 640, msSinceLastFrame);
        if (p1IsAttacking)
        {
            _player.Attack();
        }

        _scriptEngine.ExecuteAll(this);

        if (p1AddBomb)
        {
            AddBomb(_player.Position.X, _player.Position.Y, false);
        }
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var p1Position = _player!.Position;
        var p2Position = _playerCat!.Position;

        var x = (int)(p1Position.X + p2Position.X) / 2;
        var y = (int)(p1Position.Y + p2Position.Y) / 2;
        _renderer.CameraLookAt(x, y);

        RenderTerrain();
        RenderAllObjects();

        if (_isGameOver)
            RenderGameOver();
        else if (_gameWon)
            RenderGameWon();
        else
        {
            RenderTreatCounter();
            RenderLivesCounter();
        }   

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (gameObject == null) continue;

            var tempGameObject = (TemporaryGameObject)gameObject!;


            if (tempGameObject.Type.Equals("bomb") && CheckPlayerCollision(_player!, tempGameObject))
            {
                _livesLetf--;
                
            }
            if ( _livesLetf == 0)
            {
                _player.GameOver();
                _isGameOver = true;
            }


            if (tempGameObject.Type.Equals("treat"))
                {
                    TreatObject treat = (TreatObject)tempGameObject;
                    if (treat.CheckCollision((_playerCat.Position.X, _playerCat.Position.Y)))
                    {
                        _audioProcess.Start();
                        _treatsCollected++;
                        if (_treatsCollected == _treatsToWin)
                            _gameWon = true;

                        _gameObjects.Remove(treat.Id);
                        continue;

                    }
                }
        }

        _player?.Render(_renderer);
        _playerCat?.Render(_renderer);

    }
    private bool CheckPlayerCollision(PlayerObject player, TemporaryGameObject obj)
    {
        var deltaX = Math.Abs(player.Position.X - obj.Position.X);
        var deltaY = Math.Abs(player.Position.Y - obj.Position.Y);
        if (deltaX < 32 && deltaY < 32)
        {
            return true;
        }
        return false;
    }


    public void RenderTerrain()
    {
        float scaleX = 2.0f;
        float scaleY = 2.0f;  
        foreach (var currentLayer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * currentLayer.Width + i;
                    if (dataIndex == null)
                    {
                        continue;
                    }

                    var currentTileId = currentLayer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null)
                    {
                        continue;
                    }

                    var currentTile = _tileIdMap[currentTileId.Value];

                    var tileWidth = currentTile.ImageWidth ?? 0;
                    var tileHeight = currentTile.ImageHeight ?? 0;

                    var sourceRect = new Rectangle<int>(
                    0, 0,
                    (int)(currentTile.ImageWidth ?? _currentLevel.TileWidth),
                    (int)(currentTile.ImageHeight ?? _currentLevel.TileHeight)
                );

                    var destRect = new Rectangle<int>(
                        (int)(i * _currentLevel.TileWidth * scaleX),
                        (int)(j * _currentLevel.TileHeight * scaleY),
                        (int)(_currentLevel.TileWidth * scaleX),
                        (int)(_currentLevel.TileHeight * scaleY)
                    );
                    _renderer.RenderTexture(currentTile.TextureId, sourceRect, destRect);
                }
            }
        }
    }

    public void RenderGameOver()
    {
        _renderer.CameraLookAt(0, 0);

        _renderer.SetDrawColor(0, 0, 0, 180);
        _renderer.ClearScreen();

        float scale = Math.Min(960f / _gameOverTexture.Width, 640f / _gameOverTexture.Height);

        int scaledWidth = (int)(_gameOverTexture.Width * scale);
        int scaledHeight = (int)(_gameOverTexture.Height * scale);

        int x = (960 - scaledWidth) / 2;
        int y = (640 - scaledHeight) / 2;
        var destRect = new Rectangle<int>(x, y, scaledWidth, scaledHeight);
        _renderer.RenderTexture(_gameOverTextureId, new Rectangle<int>(0, 0, _gameOverTexture.Width, _gameOverTexture.Height), destRect);
    }
    public void RenderGameWon()
    {
         _renderer.CameraLookAt(0, 0);

            _renderer.SetDrawColor(0, 0, 0, 180);
            _renderer.ClearScreen();

            float scale = Math.Min(960f / _gameWonTexture.Width, 640f / _gameWonTexture.Height);

            int scaledWidth = (int)(_gameWonTexture.Width * scale);
            int scaledHeight = (int)(_gameWonTexture.Height * scale);

            int x = (960 - scaledWidth) / 2;
            int y = (640 - scaledHeight) / 2;
            var destRect = new Rectangle<int>(x, y, scaledWidth, scaledHeight);
            _renderer.RenderTexture(_gameWonTextureId, new Rectangle<int>(0, 0, _gameWonTexture.Width, _gameWonTexture.Height), destRect);
    }

    private void RenderTreatCounter()
    {
        var originalCamera = _renderer.GetCameraPosition();
        var x = originalCamera.X;
        var y = originalCamera.Y;
        _renderer.SetDrawColor(50, 50, 50, 200);
        _renderer.RenderRectangle(new Rectangle<int>(x + 20 - 240, y + 10 - 160, 320, 60));
        string counterText = $"Treats: {_treatsCollected}/{_treatsToWin}";
        _counterRenderer.DrawText(counterText, x + 35 - 240, y + 25 - 160, 3.0f);

    }
    private void RenderLivesCounter()
    {
        var originalCamera = _renderer.GetCameraPosition();
        var x = originalCamera.X;
        var y = originalCamera.Y;
        _renderer.SetDrawColor(50, 50, 50, 200);
        _renderer.RenderRectangle(new Rectangle<int>(x + 600 - 240, y+ 10 - 160, 300, 60));
        string counterText = $"Lives: {_livesLetf}/{_livesMax}";
         _counterRenderer.DrawText(counterText, x + 615 - 240, y + 25 - 160, 3.0f);

        _renderer.CameraLookAt(_player!.Position.X, _player!.Position.Y);

    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderableGameObject)
            {
                yield return renderableGameObject;
            }
        }
    }

    public (int X, int Y) GetPlayerPosition()
    {
        return _player!.Position;
    }

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        TemporaryGameObject bomb = new(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y), "bomb");
        _gameObjects.Add(bomb.Id, bomb);
    }
    public void AddTreat(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        SpriteSheet spriteSheet = SpriteSheet.Load(_renderer, "Treat.json", "Assets");
        spriteSheet.ActivateAnimation("Idle");

        TreatObject treat = new(spriteSheet, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(treat.Id, treat);
    }



}