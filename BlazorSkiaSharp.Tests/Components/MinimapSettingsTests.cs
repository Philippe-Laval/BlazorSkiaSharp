using BlazorSkiaSharp.Components;

namespace BlazorSkiaSharp.Tests.Components;

/// <summary>
/// Covers <see cref="MinimapSettings"/>, which is what gets written to and read back from
/// local storage. Parsing matters more than usual here: the value is untrusted input that
/// survives across sessions, so anything unexpected has to fall back to the defaults rather
/// than produce a broken layout.
/// </summary>
[TestClass]
public sealed class MinimapSettingsTests
{
    [TestMethod]
    public void DefaultsAreVisibleInTheBottomRight()
    {
        var settings = new MinimapSettings();

        Assert.IsTrue(settings.Expanded);
        Assert.AreEqual(MinimapCorner.BottomRight, settings.Corner);
        Assert.AreEqual(260, settings.Width);
        Assert.AreEqual(170, settings.Height);
    }

    [TestMethod]
    public void SettingsSurviveARoundTrip()
    {
        var original = new MinimapSettings(320, 200, MinimapCorner.TopLeft, false);

        var restored = MinimapSettings.Deserialize(original.Serialize());

        Assert.AreEqual(original, restored);
    }

    [TestMethod]
    public void EveryCornerSurvivesARoundTrip()
    {
        foreach (var corner in Enum.GetValues<MinimapCorner>())
        {
            var original = new MinimapSettings(300, 200, corner);

            Assert.AreEqual(original, MinimapSettings.Deserialize(original.Serialize()), $"corner {corner}");
        }
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("1,2")]
    [DataRow("1,2,3")]
    [DataRow("1,2,3,4,5")]
    [DataRow("x,y,z,w")]
    [DataRow("1,2,99,1")]
    [DataRow("1,2,-1,1")]
    [DataRow("1,2,0,2")]
    [DataRow("1,2,0,-1")]
    [DataRow("300,200,1,1,999")]
    [DataRow("300,200,1,1,")]
    public void UnusableInputFallsBackToTheDefaults(string? stored)
    {
        Assert.AreEqual(new MinimapSettings(), MinimapSettings.Deserialize(stored));
    }

    [TestMethod]
    [DataRow("-5,-5,0,1")]
    [DataRow("1,2,0,1")]
    public void AnOutOfRangeSizeIsClampedRatherThanDiscarded(string stored)
    {
        // The size is recoverable, so the user's corner and visibility choices are worth
        // keeping; throwing the whole record away would lose them for no reason.
        var restored = MinimapSettings.Deserialize(stored);

        Assert.IsGreaterThanOrEqualTo(MinimapSettings.MinWidth, restored.Width);
        Assert.IsGreaterThanOrEqualTo(MinimapSettings.MinHeight, restored.Height);
        Assert.AreEqual(MinimapCorner.TopLeft, restored.Corner);
        Assert.IsTrue(restored.Expanded);
    }

    [TestMethod]
    public void SerialisedFormIsCompactAndStable()
    {
        var serialized = new MinimapSettings(300, 200, MinimapCorner.TopRight, true).Serialize();

        Assert.AreEqual("300,200,1,1", serialized);
    }

    [TestMethod]
    public void ClampingKeepsTheSizeInsideTheSupportedRange()
    {
        var tooBig = new MinimapSettings(99999, 99999).Clamped();
        Assert.AreEqual(MinimapSettings.MaxWidth, tooBig.Width);
        Assert.AreEqual(MinimapSettings.MaxHeight, tooBig.Height);

        var tooSmall = new MinimapSettings(1, 1).Clamped();
        Assert.AreEqual(MinimapSettings.MinWidth, tooSmall.Width);
        Assert.AreEqual(MinimapSettings.MinHeight, tooSmall.Height);
    }

    [TestMethod]
    public void ClampingLeavesAValidSizeAlone()
    {
        var settings = new MinimapSettings(300, 200, MinimapCorner.BottomLeft);

        Assert.AreEqual(settings, settings.Clamped());
    }

    [TestMethod]
    public void ClampingReplacesAnUnknownCorner()
    {
        var settings = new MinimapSettings(300, 200, (MinimapCorner)42).Clamped();

        Assert.AreEqual(MinimapCorner.BottomRight, settings.Corner);
    }

    [TestMethod]
    public void DeserializeAlsoClamps()
    {
        var restored = MinimapSettings.Deserialize($"{MinimapSettings.MaxWidth + 100},1,0,1");

        Assert.AreEqual(MinimapSettings.MaxWidth, restored.Width);
        Assert.AreEqual(MinimapSettings.MinHeight, restored.Height);
    }

    [TestMethod]
    public void ConvertsToASize()
    {
        var size = new MinimapSettings(300, 150).ToSize();

        Assert.AreEqual(300f, size.Width);
        Assert.AreEqual(150f, size.Height);
    }
}
