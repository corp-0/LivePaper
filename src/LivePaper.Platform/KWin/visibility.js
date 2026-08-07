function intersects(a, b) {
    return a.x < b.x + b.width && b.x < a.x + a.width &&
        a.y < b.y + b.height && b.y < a.y + a.height;
}

function isMaximized(window) {
    const area = workspace.clientArea(KWin.MaximizeArea, window);
    return Math.abs(window.frameGeometry.x - area.x) <= 1 &&
        Math.abs(window.frameGeometry.y - area.y) <= 1 &&
        Math.abs(window.frameGeometry.width - area.width) <= 1 &&
        Math.abs(window.frameGeometry.height - area.height) <= 1;
}

function isOnCurrentDesktop(window) {
    return window.onAllDesktops || window.desktops.some(
        desktop => desktop.id === workspace.currentDesktop.id
    );
}

function isOnCurrentActivity(window) {
    return window.activities.length === 0 ||
        window.activities.indexOf(workspace.currentActivity) !== -1;
}

function publishVisibility() {
    const outputBounds = workspace.clientArea(
        KWin.FullScreenArea,
        workspace.activeScreen,
        workspace.currentDesktop
    );
    const windows = workspace.stackingOrder.filter(window =>
        window.resourceClass !== "LivePaper.Renderer" &&
        !window.minimized && !window.deleted && !window.desktopWindow &&
        !window.dock && !window.specialWindow &&
        isOnCurrentDesktop(window) && isOnCurrentActivity(window) &&
        intersects(window.frameGeometry, outputBounds)
    );
    const fullyCovered = windows.some(window => window.fullScreen || isMaximized(window));
    const state = fullyCovered
        ? "FullyCovered"
        : windows.length > 0
            ? "PartiallyCovered"
            : "Visible";
    callDBus(
        "io.github.livepaper.LivePaper",
        "/Visibility",
        "io.github.livepaper.Visibility",
        "SetVisibilityState",
        state
    );
}

function poll() {
    publishVisibility();
    callDBus(
        "io.github.livepaper.LivePaper",
        "/Visibility",
        "io.github.livepaper.Visibility",
        "NextPoll",
        poll
    );
}

poll();
