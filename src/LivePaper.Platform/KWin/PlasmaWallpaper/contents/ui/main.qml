import QtQuick
import QtWebEngine
import org.kde.plasma.plasmoid

WallpaperItem {
    id: root
    readonly property string sourceUrl: root.configuration.Source.toString()
    readonly property string serverUrl: {
        const scheme = sourceUrl.indexOf("://")
        const path = sourceUrl.indexOf("/", scheme + 3)
        return path < 0 ? sourceUrl : sourceUrl.substring(0, path)
    }

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
        activeFocusOnPress: false
        backgroundColor: "black"
        settings.playbackRequiresUserGesture: false

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
        onTriggered: root.refreshState()
    }
}
