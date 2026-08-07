let pointerCallPending = false;
let queuedPointerX = workspace.cursorPos.x;
let queuedPointerY = workspace.cursorPos.y;

function sendPointerPosition() {
    const sentX = queuedPointerX;
    const sentY = queuedPointerY;
    pointerCallPending = true;
    callDBus(
        "io.github.livepaper.LivePaper",
        "/Visibility",
        "io.github.livepaper.Visibility",
        "SetPointerPosition",
        sentX,
        sentY,
        () => {
            pointerCallPending = false;
            if (sentX !== queuedPointerX || sentY !== queuedPointerY) {
                sendPointerPosition();
            }
        }
    );
}

function publishPointerPosition() {
    queuedPointerX = workspace.cursorPos.x;
    queuedPointerY = workspace.cursorPos.y;
    if (!pointerCallPending) sendPointerPosition();
}

workspace.cursorPosChanged.connect(publishPointerPosition);
publishPointerPosition();
