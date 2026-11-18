namespace ContextNotes.SelectionProbe;

internal readonly record struct ButtonPlacement(ScreenRectangle Bounds, string Side);

internal static class OverlayPlacement
{
    public static bool IsCandidate(ScreenRectangle rectangle) =>
        rectangle.IsValid && rectangle.Width > 2 && rectangle.Height > 2;

    public static DisplayArea? ChooseDisplay(ScreenRectangle[] ranges, ScreenRectangle window, DisplayArea[] displays)
    {
        DisplayArea? result = null;
        double bestArea = 0;
        foreach (DisplayArea display in displays)
        {
            ScreenRectangle visible = window.Intersect(display.WorkArea);
            if (!visible.IsValid)
                continue;
            double area = ranges.Where(IsCandidate).Sum(rectangle => rectangle.Intersect(visible).Area);
            if (area > bestArea)
            {
                bestArea = area;
                result = display;
            }
        }
        return result;
    }

    public static ScreenRectangle? GetAnchorRectangle(ScreenRectangle[] ranges, ScreenRectangle visible,
        double scaleX, double scaleY)
    {
        ScreenRectangle[] candidates = ranges.Where(IsCandidate).Where(rect => rect.Intersects(visible)).ToArray();
        // Compare very small fragments to nearby text on the same line. A narrow character
        // with a normal line height remains a candidate, even on its own.
        double smallWidth = 4 * scaleX;
        double smallHeight = 4 * scaleY;
        double gap = 6 * scaleX;
        var primary = candidates.Where(rect => rect.Height >= smallHeight).ToArray();
        ScreenRectangle? anchor = null;
        foreach (ScreenRectangle rect in candidates)
        {
            if (rect.Width < smallWidth && rect.Height < smallHeight
                && !primary.Any(line => SameLine(rect, line, gap)))
                continue;
            ScreenRectangle clipped = rect.Intersect(visible);
            anchor = anchor is { } prior ? Union(prior, clipped) : clipped;
        }
        return anchor;
    }

    public static ButtonPlacement? Place(ScreenRectangle anchor, ScreenRectangle work, int width, int height,
        double gapX, double gapY)
    {
        if (!anchor.IsValid || !work.IsValid || width <= 0 || height <= 0)
            return null;

        // Round toward the interior of the display, then respect the text boundary.
        double xMin = Math.Ceiling(work.X);
        double yMin = Math.Ceiling(work.Y);
        double xMax = Math.Floor(work.Right) - width;
        double yMax = Math.Floor(work.Bottom) - height;
        if (xMax < xMin || yMax < yMin)
            return null;
        double x = Math.Clamp(Math.Floor(anchor.X), xMin, xMax);
        // Beside the selection the entry is top-aligned with the first selected line (upper right first).
        double y = Math.Clamp(Math.Floor(anchor.Y), yMin, yMax);
        double below = Math.Ceiling(anchor.Bottom + gapY);
        double above = Math.Floor(anchor.Y - gapY) - height;

        double right = Math.Ceiling(anchor.Right + gapX);
        if (right <= xMax)
            return new(new ScreenRectangle(right, y, width, height), "right");
        double left = Math.Floor(anchor.X - gapX) - width;
        if (left >= xMin)
            return new(new ScreenRectangle(left, y, width, height), "left");
        if (below <= yMax)
            return new(new ScreenRectangle(x, below, width, height), "below");
        return above >= yMin ? new ButtonPlacement(new ScreenRectangle(x, above, width, height), "above") : null;
    }

    private static bool SameLine(ScreenRectangle fragment, ScreenRectangle line, double gap) =>
        fragment.Y < line.Bottom && fragment.Bottom > line.Y
        && fragment.X <= line.Right + gap && fragment.Right >= line.X - gap;

    private static ScreenRectangle Union(ScreenRectangle a, ScreenRectangle b)
    {
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        return new(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }
}
