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

    /// <summary>
    /// Places an inline annotation card in priority order (right -> left -> below -> above), avoiding the selection
    /// anchor, staying within the work area above the composer, and avoiding collisions with existing annotations.
    /// </summary>
    public static ButtonPlacement? PlaceAnnotation(
        ScreenRectangle anchor,
        ScreenRectangle viewportWork,
        ScreenRectangle windowWork,
        int width,
        int height,
        double gapX,
        double gapY,
        IReadOnlyList<ScreenRectangle>? occupied = null)
    {
        occupied ??= [];
        ButtonPlacement? inViewport = PlaceAnnotationInArea(anchor, viewportWork, width, height, gapX, gapY, occupied);
        if (inViewport is not null)
            return inViewport;

        if (windowWork.IsValid && windowWork != viewportWork)
        {
            // Keep the bottom bounded by the composer-clipped viewport when using wider window margins.
            double bottomLimit = viewportWork.IsValid ? Math.Min(windowWork.Bottom, viewportWork.Bottom) : windowWork.Bottom;
            var safeWindowWork = new ScreenRectangle(windowWork.X, windowWork.Y, windowWork.Width,
                Math.Max(0, bottomLimit - windowWork.Y));
            ButtonPlacement? inWindow = PlaceAnnotationInArea(anchor, safeWindowWork, width, height, gapX, gapY, occupied);
            if (inWindow is not null)
                return inWindow;
        }
        return null;
    }

    private static ButtonPlacement? PlaceAnnotationInArea(
        ScreenRectangle anchor,
        ScreenRectangle work,
        int width,
        int height,
        double gapX,
        double gapY,
        IReadOnlyList<ScreenRectangle> occupied)
    {
        if (!anchor.IsValid || !work.IsValid || width <= 0 || height <= 0)
            return null;

        double xMin = Math.Ceiling(work.X);
        double yMin = Math.Ceiling(work.Y);
        double xMax = Math.Floor(work.Right) - width;
        double yMax = Math.Floor(work.Bottom) - height;
        if (xMax < xMin || yMax < yMin)
            return null;

        double baseX = Math.Clamp(Math.Floor(anchor.X), xMin, xMax);
        double baseY = Math.Clamp(Math.Floor(anchor.Y), yMin, yMax);
        double right = Math.Ceiling(anchor.Right + gapX);
        double left = Math.Floor(anchor.X - gapX) - width;
        double below = Math.Ceiling(anchor.Bottom + gapY);
        double above = Math.Floor(anchor.Y - gapY) - height;

        var candidates = new List<ButtonPlacement>(16);
        double stepY = Math.Max(28, Math.Round(height * 0.35));
        double stepX = Math.Max(28, Math.Round(width * 0.25));

        void AddVerticalShifts(double x, string side)
        {
            for (int i = 0; i <= 4; i++)
            {
                double downY = baseY + i * stepY;
                if (downY <= yMax)
                {
                    var rect = new ScreenRectangle(x, downY, width, height);
                    if (!rect.Intersects(anchor))
                        candidates.Add(new(rect, side));
                }
                if (i > 0)
                {
                    double upY = baseY - i * stepY;
                    if (upY >= yMin)
                    {
                        var rect = new ScreenRectangle(x, upY, width, height);
                        if (!rect.Intersects(anchor))
                            candidates.Add(new(rect, side));
                    }
                }
            }
        }

        void AddHorizontalShifts(double y, string side)
        {
            for (int i = 0; i <= 4; i++)
            {
                double rightX = baseX + i * stepX;
                if (rightX <= xMax)
                {
                    var rect = new ScreenRectangle(rightX, y, width, height);
                    if (!rect.Intersects(anchor))
                        candidates.Add(new(rect, side));
                }
                if (i > 0)
                {
                    double leftX = baseX - i * stepX;
                    if (leftX >= xMin)
                    {
                        var rect = new ScreenRectangle(leftX, y, width, height);
                        if (!rect.Intersects(anchor))
                            candidates.Add(new(rect, side));
                    }
                }
            }
        }

        if (right <= xMax)
            AddVerticalShifts(right, "right");
        if (left >= xMin)
            AddVerticalShifts(left, "left");
        if (below <= yMax)
            AddHorizontalShifts(below, "below");
        if (above >= yMin)
            AddHorizontalShifts(above, "above");

        if (candidates.Count == 0)
            return null;

        // 1. Prefer the first candidate that does not overlap any existing annotation.
        foreach (ButtonPlacement candidate in candidates)
        {
            if (occupied.All(existing => !existing.Intersects(candidate.Bounds)))
                return candidate;
        }

        // 2. If space is tight, cascade from the primary candidate so no two annotations share identical bounds.
        ButtonPlacement primary = candidates[0];
        double cascadeX = Math.Max(18, Math.Round(gapX * 2));
        double cascadeY = Math.Max(24, Math.Round(gapY * 2.4));
        for (int n = 1; n <= occupied.Count + 4; n++)
        {
            double cx = Math.Clamp(primary.Bounds.X + n * cascadeX, xMin, xMax);
            double cy = Math.Clamp(primary.Bounds.Y + n * cascadeY, yMin, yMax);
            var cascaded = new ScreenRectangle(cx, cy, width, height);
            if (!cascaded.Intersects(anchor) && occupied.All(existing => !SameOrigin(existing, cascaded)))
                return new(cascaded, primary.Side);
        }

        for (int n = 1; n <= occupied.Count + 4; n++)
        {
            double cx = Math.Clamp(primary.Bounds.X + (n % 3) * cascadeX, xMin, xMax);
            double cy = Math.Clamp(primary.Bounds.Y + n * cascadeY, yMin, yMax);
            var cascaded = new ScreenRectangle(cx, cy, width, height);
            if (occupied.All(existing => !SameOrigin(existing, cascaded)))
                return new(cascaded, primary.Side);
        }

        return primary;
    }

    public static ScreenRectangle ClampToArea(ScreenRectangle rectangle, ScreenRectangle area)
    {
        if (!rectangle.IsValid || !area.IsValid)
            return rectangle;
        double width = Math.Min(rectangle.Width, area.Width);
        double height = Math.Min(rectangle.Height, area.Height);
        double xMin = Math.Ceiling(area.X);
        double yMin = Math.Ceiling(area.Y);
        double xMax = Math.Max(xMin, Math.Floor(area.Right) - width);
        double yMax = Math.Max(yMin, Math.Floor(area.Bottom) - height);
        return new ScreenRectangle(
            Math.Clamp(Math.Round(rectangle.X), xMin, xMax),
            Math.Clamp(Math.Round(rectangle.Y), yMin, yMax),
            width,
            height);
    }

    private static bool SameOrigin(ScreenRectangle a, ScreenRectangle b) =>
        Math.Abs(a.X - b.X) < 8 && Math.Abs(a.Y - b.Y) < 8;

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
