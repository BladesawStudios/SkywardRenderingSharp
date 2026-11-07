namespace SkywardRenderingSharp.Tests;

public class AnimationTests
{
    [RomFact]
    public void EveryClipInTheCommonLibraryIsFullyReadable()
    {
        var library = Romfs.Rom!.OpenAnimations("PLCommonAnime");
        int clips = 0;
        foreach (string name in library.ClipNames)
        {
            var clip = library.Get(name);
            Assert.True(clip.UnreadableTracks == 0, $"{name}: {string.Join("; ", clip.UnreadableDetails.Take(2))}");
            Assert.True(clip.FrameCount > 0, name);
            clips++;
        }
        Assert.True(clips >= 300);
    }

    [RomFact]
    public void ClipsMoveTheSkeletonAndKeepItFinite()
    {
        var rom = Romfs.Rom!;
        var model = SkywardRenderingSharp.Model.Mdl0Model.Read(rom.Open(rom.Index.Find("al")[0]).Bres, "al");
        var clip = rom.OpenAnimations("PLCommonAnime").Get("BattleReady");
        var a = clip.Evaluate(model.Bones, 0); var b = clip.Evaluate(model.Bones, clip.FrameCount / 2f);
        Assert.All(b.World, m => Assert.True(float.IsFinite(m.M41) && float.IsFinite(m.M11)));
        Assert.Contains(Enumerable.Range(0, model.Bones.Count), i => (a.World[i].Translation - b.World[i].Translation).Length() > 0.5f);
    }

    [RomFact]
    public void ExpressionPatternsSwapEyeTextures()
    {
        var library = Romfs.Rom!.OpenAnimations("PLCommonAnime");
        Assert.NotEmpty(library.PatternNames);
        var pattern = library.GetPattern(library.PatternNames.First());
        Assert.NotEmpty(pattern.Evaluate(0));
    }

    [RomFact]
    public void GazeClipsTranslateTheEyeballTexture()
    {
        var clip = Romfs.Rom!.OpenAnimations("PLCommonAnime").GetSrt("F_Notice");
        var start = clip.Evaluate(0); var later = clip.Evaluate(clip.FrameCount / 2f);
        Assert.True(start.ContainsKey("al_eyeballL_m"));
        Assert.NotEqual(start["al_eyeballL_m"][0].Translation, later["al_eyeballL_m"][0].Translation);
    }
}
