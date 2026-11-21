namespace MmdStudio.Poc
{
    // H1-04: file dialogs behind one seam. The workspace asks this interface; the Player uses the
    // Windows common dialogs, probes substitute scripted answers so toolbar and inspector buttons
    // can be exercised end to end. Every method returns a full path, or null when cancelled.
    internal interface IStudioFileDialogs
    {
        string OpenModel();
        string OpenMotion();
        string OpenScene();
        string OpenAudio();
        string SaveScene(string suggestedName);
        string SaveText(string suggestedName);
    }

    internal sealed class WindowsStudioFileDialogs : IStudioFileDialogs
    {
        public string OpenModel() => WindowsRuntimeFilePicker.Open(true);
        public string OpenMotion() => WindowsRuntimeFilePicker.Open(false);
        public string OpenScene() => WindowsRuntimeFilePicker.OpenScene();
        public string OpenAudio() => WindowsRuntimeFilePicker.OpenAudio();
        public string SaveScene(string suggestedName) => WindowsRuntimeFilePicker.SaveScene(suggestedName);
        public string SaveText(string suggestedName) => WindowsRuntimeFilePicker.SaveText(suggestedName);
    }
}
