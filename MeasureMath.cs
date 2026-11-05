using System;
using System.Drawing;

namespace MeasuringStick;

/// Pure geometry for a measured line. Screen Y points down; angles are reported the way people read them (up = positive).
public static class MeasureMath
{
    public static double Distance(Point a, Point b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// Direction of a->b in degrees, 0-360, counter-clockwise from "right" (so straight up is 90).
    public static double Direction(Point a, Point b)
    {
        if (a == b) return 0;
        double deg = Math.Atan2(-(b.Y - a.Y), b.X - a.X) * 180.0 / Math.PI;
        return (deg + 360.0) % 360.0;
    }

    /// Smallest angle between the line and a horizontal line, 0-90.
    public static double FromHorizontal(Point a, Point b)
    {
        double d = Direction(a, b) % 180.0;
        return d > 90.0 ? 180.0 - d : d;
    }

    /// Smallest angle between the line and a vertical line, 0-90.
    public static double FromVertical(Point a, Point b) => 90.0 - FromHorizontal(a, b);

    /// Keeps the length of a->b but rounds its direction to the nearest step (e.g. 15 degrees).
    public static Point SnapAngle(Point a, Point b, double stepDeg)
    {
        double len = Distance(a, b);
        if (len < 1) return b;
        double step = stepDeg * Math.PI / 180.0;
        double ang = Math.Round(Math.Atan2(b.Y - a.Y, b.X - a.X) / step) * step;
        return new Point(a.X + (int)Math.Round(len * Math.Cos(ang)), a.Y + (int)Math.Round(len * Math.Sin(ang)));
    }

    /// One line of text with every value, for the clipboard.
    public static string Summary(Point a, Point b, Point screenOffset)
    {
        var s = new Point(a.X + screenOffset.X, a.Y + screenOffset.Y);
        var e = new Point(b.X + screenOffset.X, b.Y + screenOffset.Y);
        return $"Distance {Distance(a, b):F1} px, W {Math.Abs(b.X - a.X)} px, H {Math.Abs(b.Y - a.Y)} px, " +
               $"{FromHorizontal(a, b):F1}° from horizontal, {FromVertical(a, b):F1}° from vertical, " +
               $"direction {Direction(a, b):F1}°, from ({s.X}, {s.Y}) to ({e.X}, {e.Y})";
    }
}
