import QtQuick
import QtWebEngine
import org.kde.plasma.plasmoid
import "input" as LivePaperInput

WallpaperItem {
    id: root
    readonly property string packageVersion: "@LIVEPAPER_PACKAGE_VERSION@"
    property bool pointerInput: false

    function reportVersion() {
        root.configuration.LoadedVersion = packageVersion + ":" + inputBridge.buildVersion
        root.configuration.ReportedCheck = root.configuration.VersionCheck
    }
    readonly property string sourceUrl: root.configuration.Source.toString()
    readonly property string serverUrl: {
        const scheme = sourceUrl.indexOf("://")
        const path = sourceUrl.indexOf("/", scheme + 3)
        return path < 0 ? sourceUrl : sourceUrl.substring(0, path)
    }

    function refreshInput() {
        pointerInput = false
        const source = serverUrl
        if (source.length === 0) {
            return
        }
        const request = new XMLHttpRequest()
        request.open("GET", source + "/__livepaper/config.json")
        request.onreadystatechange = () => {
            if (request.readyState === XMLHttpRequest.DONE && request.status === 200 && source === serverUrl) {
                pointerInput = JSON.parse(request.responseText).pointerInput === true
            }
        }
        request.send()
    }

    onServerUrlChanged: refreshInput()
    Component.onCompleted: refreshInput()

    function refreshState() {
        if (serverUrl.length === 0) {
            return
        }

        const request = new XMLHttpRequest()
        request.open("GET", serverUrl + "/__livepaper/visibility")
        request.onreadystatechange = () => {
            if (request.readyState !== XMLHttpRequest.DONE || request.status !== 200) {
                return
            }

            const visibility = JSON.parse(request.responseText)
            webView.audioMuted = visibility.shouldMute
            webView.lifecycleState = visibility.shouldRender
                ? WebEngineView.LifecycleState.Active
                : WebEngineView.LifecycleState.Frozen
        }
        request.send()
    }

    WebEngineView {
        id: webView
        anchors.fill: parent
        url: root.configuration.Source
        visible: url.toString().length > 0
        enabled: root.pointerInput
        backgroundColor: "black"
        settings.playbackRequiresUserGesture: false

        LivePaperInput.MouseInput { id: inputBridge }

        onContextMenuRequested: request => request.accepted = true

        onUrlChanged: {
            lifecycleState = WebEngineView.LifecycleState.Active
            stateTimer.restart()
        }

        onLoadingChanged: request => {
            if (request.status === WebEngineView.LoadFailedStatus) {
                retryTimer.restart()
            }
        }
    }

    Timer {
        id: retryTimer
        interval: 1000
        onTriggered: webView.reload()
    }

    Timer {
        id: stateTimer
        interval: 500
        repeat: true
        running: root.serverUrl.length > 0
        onTriggered: {
            root.reportVersion()
            root.refreshState()
        }
    }
}
