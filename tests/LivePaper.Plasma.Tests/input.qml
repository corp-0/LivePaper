import QtQuick
import QtWebEngine
import "input" as LivePaperInput

Item {
    id: root
    width: 400
    height: 300
    property bool loaded: false
    property bool pointerInput: true
    property bool overlayEnabled: true
    property int desktopPresses: 0
    property int desktopReleases: 0
    property int desktopWheels: 0
    signal metricsReady(string json)

    WebEngineView {
        id: web
        objectName: "web"
        x: 30
        y: 20
        width: 300
        height: 200
        enabled: root.pointerInput
        LivePaperInput.MouseInput { objectName: "inputBridge" }
        onContextMenuRequested: request => request.accepted = true
        onLoadingChanged: request => {
            root.loaded = request.status === WebEngineView.LoadSucceededStatus
        }
        Component.onCompleted: loadHtml(`<!doctype html><html><body style="margin:0">
            <button style="width:300px;height:200px" id="button">Click</button>
            <script>
                window.metrics = {mousedown:0, mouseup:0, click:0, dblclick:0, mousemove:0, wheel:0, buttonClicks:0, trusted:true};
                document.getElementById('button').addEventListener('click', () => metrics.buttonClicks++);
                for (const type of ['mousedown','mouseup','click','dblclick','mousemove','wheel']) {
                    window.addEventListener(type, e => {
                        metrics[type]++;
                        metrics.trusted = metrics.trusted && e.isTrusted;
                        metrics.x = e.clientX;
                        metrics.y = e.clientY;
                        metrics.button = e.button;
                        metrics.target = e.target.id || e.target.nodeName;
                        metrics.buttons = e.buttons;
                        metrics.shift = e.shiftKey;
                        if (type === 'wheel') metrics.deltaY = e.deltaY;
                    });
                }
            </script></body></html>`)
    }
    MouseArea {
        anchors.fill: parent
        enabled: root.overlayEnabled
        hoverEnabled: true
        acceptedButtons: Qt.AllButtons
        onPressed: root.desktopPresses++
        onReleased: root.desktopReleases++
        onWheel: wheel => { root.desktopWheels++; wheel.accepted = true }
    }
    function readMetrics() {
        web.runJavaScript('JSON.stringify(window.metrics)', result => root.metricsReady(result || '{}'))
    }
}
