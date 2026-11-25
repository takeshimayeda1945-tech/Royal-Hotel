using System.Security.Cryptography;
using System.Text;

namespace Intouch_67173986_L003.Models;

/// <summary>
/// Draws a QR-looking SVG for the payment page demo. It is NOT a scannable PromptPay code:
/// the modules are a hash of the payload, framed by the three QR finder squares.
/// </summary>
public static class DemoQr
{
    private const int Size = 25;

    public static string Svg(string payload, int pixels = 200)
    {
        var bits = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var random = new Random(BitConverter.ToInt32(bits, 0));
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"-2 -2 {Size + 4} {Size + 4}\" width=\"{pixels}\" height=\"{pixels}\" role=\"img\" aria-label=\"QR Code ตัวอย่าง\" shape-rendering=\"crispEdges\">");
        svg.Append($"<rect x=\"-2\" y=\"-2\" width=\"{Size + 4}\" height=\"{Size + 4}\" fill=\"#fff\"/>");

        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!IsFinderArea(x, y) && random.Next(2) == 1)
                {
                    svg.Append($"<rect x=\"{x}\" y=\"{y}\" width=\"1\" height=\"1\" fill=\"#000\"/>");
                }
            }
        }

        foreach (var (fx, fy) in new[] { (0, 0), (Size - 7, 0), (0, Size - 7) })
        {
            svg.Append($"<rect x=\"{fx}\" y=\"{fy}\" width=\"7\" height=\"7\" fill=\"#000\"/>");
            svg.Append($"<rect x=\"{fx + 1}\" y=\"{fy + 1}\" width=\"5\" height=\"5\" fill=\"#fff\"/>");
            svg.Append($"<rect x=\"{fx + 2}\" y=\"{fy + 2}\" width=\"3\" height=\"3\" fill=\"#000\"/>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static bool IsFinderArea(int x, int y)
    {
        return (x < 8 && y < 8) || (x >= Size - 8 && y < 8) || (x < 8 && y >= Size - 8);
    }
}
