using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Treesi.Desktop.Graph;

public sealed class GraphLaneCanvas : Control
{
    public static readonly StyledProperty<GraphRow?> RowProperty = AvaloniaProperty.Register<GraphLaneCanvas, GraphRow?>(nameof(Row));
    public GraphRow? Row { get => GetValue(RowProperty); set => SetValue(RowProperty, value); }
    static GraphLaneCanvas() => AffectsRender<GraphLaneCanvas>(RowProperty);
    public GraphLaneCanvas() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Row is not { } row) return;
        var height = Bounds.Height; var middle = height / 2;
        double X(int lane) => 12 + lane * Math.Min(18, (Bounds.Width - 24) / Math.Max(1, row.LaneCount - 1));
        IBrush Color(int lane) => (IBrush?)this.FindResource(ActualThemeVariant, new[] { "Accent.Primary", "Code.Keyword", "Code.String", "Diff.HunkText" }[lane % 4]) ?? Brushes.Gray;
        foreach (var segment in row.Segments)
        {
            var pen = new Pen(Color(segment.To), 1.7);
            var from = new Point(X(segment.From), segment.Through || segment.Incoming ? 0 : middle);
            var to = new Point(X(segment.To), segment.Incoming ? middle : height);
            if (segment.From == segment.To) context.DrawLine(pen, from, to);
            else
            {
                var geometry = new StreamGeometry();
                using (var drawing = geometry.Open())
                { drawing.BeginFigure(from, false); drawing.CubicBezierTo(new(from.X, height - 2), new(to.X, middle + 2), to); drawing.EndFigure(false); }
                context.DrawGeometry(null, pen, geometry);
            }
        }
        var background = (IBrush?)this.FindResource(ActualThemeVariant, "Surface.Window") ?? Brushes.Black;
        context.DrawEllipse(background, new Pen(Color(row.Lane), 2), new(X(row.Lane), middle), row.Commit.Parents.Count > 1 ? 5 : 3.5, row.Commit.Parents.Count > 1 ? 5 : 3.5);
        if (row.Last && row.Continues)
            foreach (var segment in row.Segments.Where(item => !item.Incoming))
                context.DrawLine(new Pen(Color(segment.To), 2, new DashStyle([1d, 1d], 0)), new(X(segment.To), height - 5), new(X(segment.To), height));
    }
}
