namespace C12ProjetoCiv.Rendering;

public readonly record struct UiCommand(
    int? RequestedWorkerCount,
    bool TogglePause,
    bool Restart,
    bool ToggleStressMode,
    bool ToggleFullscreenMode);
