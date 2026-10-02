using System.Drawing.Drawing2D;
using Wolf.Editors;

namespace DuelIt
{
    /// <summary>DuelIt's cards: drawn by the painter WolfX shares (Tools\Shared\Editors\CardFacePainter.cs), from the duel's game data.</summary>
    public static class CardPainter
    {
        public const float Aspect = CardFacePainter.Aspect;

        public static void DrawBack(Graphics g, GameData data, Rectangle r)
        {
            if (data.CardBack is { } back)
                g.DrawImage(back, r);
            else
            {
                using var brush = new LinearGradientBrush(r, Color.FromArgb(120, 70, 30), Color.FromArgb(60, 30, 10), 90f);
                g.FillRectangle(brush, r);
            }
        }

        /// <summary>Draws card id into r (card-shaped). fallbackName is shown when the card's data or art is missing.</summary>
        public static void Draw(Graphics g, GameData data, Rectangle r, int id, string? fallbackName = null)
        {
            var info = data.Card(id);
            var face = info == null ? null : new CardFace(info.Frame, info.Attribute, info.Level, info.Atk, info.Def);
            CardFacePainter.Draw(g, data, r, face, id > 0 ? data.Art(id) : null, info?.Name ?? fallbackName ?? (id > 0 ? $"#{id}" : "?"));
        }
    }
}
