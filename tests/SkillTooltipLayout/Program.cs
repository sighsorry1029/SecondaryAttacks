using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

if (args.Length != 2)
{
    Console.Error.WriteLine("Expected the compiled SecondaryAttacks.dll path and Valheim game directory. Use Run-Tests.ps1.");
    return 2;
}

string assemblyPath = Path.GetFullPath(args[0]);
string gameDirectory = Path.GetFullPath(args[1]);
string[] dependencyDirectories =
{
    Path.GetDirectoryName(assemblyPath)!,
    Path.Combine(gameDirectory, "valheim_Data", "Managed"),
    Path.Combine(gameDirectory, "BepInEx", "core")
};
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (string directory in dependencyDirectories)
    {
        string path = Path.Combine(directory, name.Name + ".dll");
        if (File.Exists(path))
        {
            return context.LoadFromAssemblyPath(path);
        }
    }

    return null;
};

Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type layoutType = mod.GetType("SecondaryAttacks.SecondaryAttackSkillTooltipLayout", throwOnError: false)
    ?? throw new InvalidOperationException("The selected DLL does not contain SecondaryAttackSkillTooltipLayout. Build the current production project first.");
const BindingFlags methodFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
T Bind<T>(string name) where T : Delegate =>
    (layoutType.GetMethod(name, methodFlags)
     ?? throw new MissingMethodException(layoutType.FullName, name)).CreateDelegate<T>();

float ReadConstant(string name) =>
    (float)(layoutType.GetField(name, methodFlags)?.GetRawConstantValue()
        ?? throw new MissingFieldException(layoutType.FullName, name));
float bodyWidth = ReadConstant("BodyWidth");
float padding = ReadConstant("Padding");
float width = ReadConstant("Width");
Func<Rect, Vector2, float> getScale = Bind<Func<Rect, Vector2, float>>("GetScale");
Func<Rect, Rect, float, Vector2, Vector2> getTopLeft =
    Bind<Func<Rect, Rect, float, Vector2, Vector2>>("GetTopLeft");

Console.WriteLine("DLL: " + assemblyPath);
Console.WriteLine("Version: " + mod.GetName().Version);
Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assemblyPath))));

const float epsilon = 0.002f;
int assertions = 0;
int groups = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition)
    {
        throw new InvalidOperationException(description);
    }
}

void Near(float actual, float expected, string description) =>
    Check(float.IsFinite(actual) && Math.Abs(actual - expected) <= epsilon,
        $"{description}: expected {expected}, actual {actual}");

void Contained(Rect viewport, Vector2 topLeft, Vector2 size, string description)
{
    Check(float.IsFinite(topLeft.x) && float.IsFinite(topLeft.y), description + ": finite position");
    Check(size.x >= 0f && size.y >= 0f, description + ": nonnegative dimensions");
    Check(topLeft.x >= viewport.xMin + 12f - epsilon, description + ": left inset");
    Check(topLeft.x + size.x <= viewport.xMax - 12f + epsilon, description + ": right inset");
    Check(topLeft.y <= viewport.yMax - 12f + epsilon, description + ": top inset");
    Check(topLeft.y - size.y >= viewport.yMin + 12f - epsilon, description + ": bottom inset");
}

void Run(string name, Action body)
{
    int before = assertions;
    body();
    groups++;
    Console.WriteLine($"PASS {name}: {assertions - before} assertions");
}

Run("panel gap, shared left alignment, and row tracking", () =>
{
    Rect viewport = new(-960f, -540f, 1920f, 1080f);
    Rect panel = new(100f, -350f, 600f, 700f);
    Vector2 size = new(width, 150f);
    Vector2 first = getTopLeft(viewport, panel, 210f, size);
    Vector2 second = getTopLeft(viewport, panel, -20f, size);
    Near(first.x, -190f, "panel left minus 8 UI unit gap and tooltip width");
    Near(first.y, 210f, "first row top");
    Near(second.x, first.x, "different skill rows share the tooltip left edge");
    Near(second.y, -20f, "second row top");
    Near(second.y - first.y, -230f, "row displacement carries through unchanged");
    Contained(viewport, first, size, "first row");
    Contained(viewport, second, size, "second row");
});

Run("all four viewport boundaries", () =>
{
    Rect viewport = new(-960f, -540f, 1920f, 1080f);
    Vector2 size = new(width, 150f);
    Rect leftPanel = new(-940f, -350f, 600f, 700f);
    Rect rightPanel = new(2000f, -350f, 600f, 700f);
    Vector2 leftTop = getTopLeft(viewport, leftPanel, 10000f, size);
    Vector2 rightBottom = getTopLeft(viewport, rightPanel, -10000f, size);
    Near(leftTop.x, -948f, "left clamp");
    Near(leftTop.y, 528f, "top clamp");
    Near(rightBottom.x, 666f, "right clamp");
    Near(rightBottom.y, -378f, "bottom clamp");
    Contained(viewport, leftTop, size, "left/top corner");
    Contained(viewport, rightBottom, size, "right/bottom corner");
});

Run("negative canvas coordinates", () =>
{
    Rect viewport = new(-1200f, -800f, 900f, 500f);
    Rect panel = new(-720f, -730f, 300f, 300f);
    Vector2 size = new(width, 100f);
    Vector2 position = getTopLeft(viewport, panel, -470f, size);
    Near(position.x, -1010f, "negative-coordinate left placement");
    Near(position.y, -470f, "negative-coordinate row top");
    Contained(viewport, position, size, "negative-coordinate tooltip");
});

Run("uniform downscaling without upscaling", () =>
{
    Rect viewport = new(0f, 0f, 240f, 160f);
    Rect panel = new(180f, 20f, 300f, 100f);
    Near(getScale(viewport, new Vector2(100f, 100f)), 1f, "fitting content is not enlarged");
    Near(getScale(viewport, new Vector2(1000f, 40f)), 0.216f, "wide content fits the available width");
    Near(getScale(viewport, new Vector2(40f, 680f)), 0.2f, "tall content fits the available height");
    Vector2 originalSize = new(720f, 680f);
    float scale = getScale(viewport, originalSize);
    Near(scale, 0.2f, "oversized content scale");
    Vector2 scaledSize = originalSize * scale;
    Near(scaledSize.x / originalSize.x, scaledSize.y / originalSize.y, "aspect ratio is preserved");
    Contained(viewport, getTopLeft(viewport, panel, 1000f, scaledSize), scaledSize, "scaled oversized content");
});

Run("vanilla body width plus separate padding", () =>
{
    Near(bodyWidth, 250f, "body layout retains vanilla's 250 UI units");
    Near(padding, 16f, "each background side adds 16 UI units");
    Near(width, 282f, "outer width includes separate left and right padding");
    Near(width - padding * 2f, bodyWidth, "padding does not consume the vanilla text width");

    Rect narrowViewport = new(0f, 0f, 200f, 300f);
    Rect panel = new(50f, 100f, 600f, 120f);
    Vector2 layoutSize = new(width, 100f);
    float scale = getScale(narrowViewport, layoutSize);
    Near(scale, 176f / 282f, "narrow viewport uniformly scales the outer width");
    Near(layoutSize.x, 282f, "scaling leaves the original layout width intact");
    Near(layoutSize.x - padding * 2f, 250f, "scaling does not reduce the body layout width");
    Vector2 scaledSize = layoutSize * scale;
    Near(scaledSize.x, 176f, "scaled outer width fits available viewport width");
    Near(scaledSize.x / layoutSize.x, scaledSize.y / layoutSize.y,
        "width and height use the same scale");
    Contained(narrowViewport, getTopLeft(narrowViewport, panel, 200f, scaledSize), scaledSize,
        "narrow viewport tooltip");
});

Run("viewport, panel, row, and content size matrix", () =>
{
    Rect[] viewports =
    {
        new(-960f, -540f, 1920f, 1080f),
        new(0f, 0f, 800f, 600f),
        new(-1800f, -1200f, 900f, 600f),
        new(0f, 0f, 320f, 180f),
        new(0f, 0f, 200f, 300f)
    };
    foreach (Rect viewport in viewports)
    {
        Rect[] panels =
        {
            new(viewport.xMin + 30f, viewport.yMin + 30f, 300f, 200f),
            new(viewport.center.x, viewport.yMin, 600f, 700f),
            new(viewport.xMax + 500f, viewport.yMax, 1200f, 700f)
        };
        float[] rowTops = { viewport.yMin - 500f, viewport.yMin + 37f, viewport.center.y, viewport.yMax - 12f, viewport.yMax + 500f };
        foreach (Rect panel in panels)
        {
            foreach (float height in new[] { 100f, viewport.height * 2f })
            {
                Vector2 originalSize = new(width, height);
                float scale = getScale(viewport, originalSize);
                Check(float.IsFinite(scale) && scale > 0f && scale <= 1f,
                    "matrix scale is finite, positive, and does not enlarge content");
                Vector2 size = originalSize * scale;
                foreach (float rowTop in rowTops)
                {
                    Contained(viewport, getTopLeft(viewport, panel, rowTop, size), size, "matrix layout");
                }
            }
        }
    }
});

Run("centered section headings preserve original descriptions and body tokens", () =>
{
    Type tooltipType = mod.GetType("SecondaryAttacks.SecondaryAttackSkillTooltipSystem", throwOnError: true)!;
    var appendSection = (tooltipType.GetMethod("AppendSection", methodFlags)
        ?? throw new MissingMethodException(tooltipType.FullName, "AppendSection"))
        .CreateDelegate<Func<string, StringBuilder, string>>();
    (string heading, string body)[] sections =
    {
        ("$sa_skill_tooltip_sneak_heading", "$sa_skill_tooltip_sneak_backstab_gain\n$sa_skill_tooltip_sneak_ambush"),
        ("$sa_skill_tooltip_blood_magic_heading", "$sa_skill_tooltip_blood_magic_cooldown\n$sa_skill_tooltip_blood_magic_lifetime")
    };
    foreach ((string heading, string body) in sections)
    {
        foreach (string original in new[] { string.Empty, "$skill_description", "Existing description\nSecond original line" })
        {
            StringBuilder section = new(heading + "\n" + body);
            string actual = appendSection(original, section);
            string expected = (original.Length > 0 ? original + "\n\n" : string.Empty)
                + "<align=center>" + heading + "\n</align>" + body;
            Check(actual == expected, "only the section heading is enclosed by centered alignment; original and body stay intact");
            Check(section.ToString() == heading + "\n" + body, "formatting does not mutate the supplied section");
        }
    }
});

Console.WriteLine($"PASS {groups} groups, {assertions} assertions against the compiled production helpers.");
Console.WriteLine("These are managed geometry and text-format checks, not Unity UI rendering or gameplay tests.");
return 0;
