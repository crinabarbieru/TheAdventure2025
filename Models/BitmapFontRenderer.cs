using System;
using Silk.NET.Maths;
using Silk.NET.SDL;

namespace TheAdventure.Models;

public class BitmapFontRenderer
{
    private readonly SpriteSheet _fontSpriteSheet;
    private readonly GameRenderer _renderer;

    public BitmapFontRenderer(GameRenderer renderer, SpriteSheet fontSpriteSheet)
    {
        _renderer = renderer;
        _fontSpriteSheet = fontSpriteSheet;
        
        if (_fontSpriteSheet.FrameWidth <= 0 || _fontSpriteSheet.FrameHeight <= 0)
        {
            throw new ArgumentException("SpriteSheet must have valid frame dimensions");
        }
    }

    public void DrawText(string text, int x, int y, float scale = 1.0f, RendererFlip flip = RendererFlip.None)
    {
        int currentX = x;
        
        foreach (char c in text.ToUpper())
        {
            if (c == ' ')
            {
                currentX += (int)(_fontSpriteSheet.FrameWidth * scale);
                continue;
            }

            var (row, col) = GetCharPosition(c);
            if (row == -1 || col == -1) continue;

            var textureId = _fontSpriteSheet.TextureId;
            var intScale = (int)scale;
            _renderer.RenderTexture(
                textureId,
                new Rectangle<int>(
                    col * _fontSpriteSheet.FrameWidth, 
                    row * _fontSpriteSheet.FrameHeight, 
                    _fontSpriteSheet.FrameWidth, 
                    _fontSpriteSheet.FrameHeight),
                new Rectangle<int>(
                    currentX, 
                    y, 
                    (int)(_fontSpriteSheet.FrameWidth * scale), 
                    (int)(_fontSpriteSheet.FrameHeight * scale)),
                flip
            );
            currentX += (int)(_fontSpriteSheet.FrameWidth * scale);
        }
    }


    private (int row, int col) GetCharPosition(char c)
    {
        // SpriteSheet Row 0: A-P 
        if (c >= 'A' && c <= 'P') return (0, c - 'A');
        
        // SpriteSheet Row 1: Q-Z & 1-6 digits
        if (c >= 'Q' && c <= 'Z') return (1, c - 'Q');
        if (c == '1') return (1, 10); // Example symbol position
        if (c == '2') return (1, 11);
        if (c == '3') return (1, 12);
        if (c == '4') return (1, 13);
        if (c == '5') return (1, 14);
        if (c == '6') return (1, 15);

        // SpriteSheet Row 2: 7-0 & other symbols
        if (c >= '7' && c <= '9' || c=='0')
        {
            return (2, "7890".IndexOf(c));
        }
        if (c == ':') return (2, 9);
        if (c == '/') return (3, 0);
        
        
        return (-1, -1); 
    }
}