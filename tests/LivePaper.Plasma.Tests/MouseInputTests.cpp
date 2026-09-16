#include <QGuiApplication>
#include <QFile>
#include <QFileInfo>
#include <QDir>
#include <QJsonDocument>
#include <QJsonObject>
#include <QQuickItem>
#include <QQuickView>
#include <QSignalSpy>
#include <QTest>
#include <QtWebEngineQuick/qtwebenginequickglobal.h>
#include <memory>

class MouseInputTests : public QObject
{
    Q_OBJECT

private:
    std::unique_ptr<QQuickView> view;

    QJsonObject metrics()
    {
        QSignalSpy ready(view->rootObject(), SIGNAL(metricsReady(QString)));
        QMetaObject::invokeMethod(view->rootObject(), "readMetrics");
        if (ready.isEmpty() && !ready.wait(5000))
        {
            QTest::qFail("WebEngine did not return input metrics", __FILE__, __LINE__);
            return {};
        }
        return QJsonDocument::fromJson(ready.first().first().toString().toUtf8()).object();
    }

private slots:
    void init()
    {
        view = std::make_unique<QQuickView>();
        view->setSource(QUrl::fromLocalFile(QStringLiteral(TEST_QML_PATH)));
        QVERIFY2(view->status() == QQuickView::Ready, "Could not load the input test scene");
        view->show();
        QVERIFY(QTest::qWaitForWindowExposed(view.get()));
        QTRY_VERIFY_WITH_TIMEOUT(view->rootObject()->property("loaded").toBool(), 10000);
    }

    void cleanup()
    {
        view.reset();
    }

    void reportsTheLoadedNativeBuildVersion()
    {
        auto *bridge = view->rootObject()->findChild<QQuickItem *>("inputBridge");
        QVERIFY(bridge);
        QFile version(QFileInfo(QStringLiteral(TEST_QML_PATH)).dir().filePath("input/input-version.txt"));
        QVERIFY(version.open(QIODevice::ReadOnly));
        QCOMPARE(bridge->property("buildVersion").toString(), QString::fromUtf8(version.readAll()).trimmed());
    }

    void forwardsTrustedClickWithoutConsumingDesktopInput()
    {
        QTest::mouseClick(view.get(), Qt::LeftButton, Qt::ShiftModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["click"].toInt(), 1);
        const auto result = metrics();
        QCOMPARE(result["mousedown"].toInt(), 1);
        QCOMPARE(result["mouseup"].toInt(), 1);
        QVERIFY(result["trusted"].toBool());
        QVERIFY(result["shift"].toBool());
        QCOMPARE(result["x"].toInt(), 50);
        QCOMPARE(result["y"].toInt(), 50);
        QCOMPARE(view->rootObject()->property("desktopPresses").toInt(), 1);
        QCOMPARE(view->rootObject()->property("desktopReleases").toInt(), 1);
    }

    void doesNotDuplicateInputWithoutDesktopOverlay()
    {
        view->rootObject()->setProperty("overlayEnabled", false);
        QTest::mouseClick(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["click"].toInt(), 1);
        QTest::qWait(100);
        const auto result = metrics();
        QCOMPARE(result["mousedown"].toInt(), 1);
        QCOMPARE(result["mouseup"].toInt(), 1);
        QCOMPARE(result["click"].toInt(), 1);
    }

    void disabledWallpaperLeavesDesktopInputAlone()
    {
        view->rootObject()->setProperty("pointerInput", false);
        QTest::mouseClick(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QCOMPARE(metrics()["mousedown"].toInt(), 0);
        QCOMPARE(view->rootObject()->property("desktopPresses").toInt(), 1);
    }

    void forwardsDoubleClickOnce()
    {
        QTest::mouseDClick(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["dblclick"].toInt(), 1);
        QCOMPARE(metrics()["mousedown"].toInt(), 2);
        QCOMPARE(metrics()["mouseup"].toInt(), 2);
    }

    void forwardsRightButtonWithoutConsumingDesktopInput()
    {
        QTest::mouseClick(view.get(), Qt::RightButton, Qt::NoModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["mouseup"].toInt(), 1);
        QCOMPARE(metrics()["button"].toInt(), 2);
        QCOMPARE(view->rootObject()->property("desktopPresses").toInt(), 1);
    }

    void releasesButtonsWhenInputIsDisabled()
    {
        QTest::mousePress(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["mousedown"].toInt(), 1);
        view->rootObject()->setProperty("pointerInput", false);
        QTRY_COMPARE(metrics()["mouseup"].toInt(), 1);
        QCOMPARE(metrics()["buttons"].toInt(), 0);
        QTest::mouseRelease(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
    }

    void ignoresInputOutsideWallpaper()
    {
        QTest::mouseClick(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(10, 10));
        QCOMPARE(metrics()["mousedown"].toInt(), 0);
        QCOMPARE(view->rootObject()->property("desktopPresses").toInt(), 1);
    }

    void forwardsDragAndReleaseOutsideWallpaper()
    {
        QTest::mousePress(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QTest::mouseMove(view.get(), QPoint(90, 80));
        QTRY_VERIFY(metrics()["mousemove"].toInt() > 0);
        QCOMPARE(metrics()["buttons"].toInt(), 1);
        QTest::mouseRelease(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(380, 280));
        QTRY_COMPARE(metrics()["mouseup"].toInt(), 1);
        QCOMPARE(metrics()["buttons"].toInt(), 0);
    }

    void forwardsWheelWithoutConsumingDesktopInput()
    {
        const QPointF position(80, 70);
        QWheelEvent wheel(position, view->mapToGlobal(position.toPoint()), {}, QPoint(0, 120),
            Qt::NoButton, Qt::ControlModifier, Qt::NoScrollPhase, false);
        QCoreApplication::sendEvent(view.get(), &wheel);
        QTRY_COMPARE(metrics()["wheel"].toInt(), 1);
        QVERIFY(metrics()["deltaY"].toDouble() < 0);
        QVERIFY(metrics()["trusted"].toBool());
        QCOMPARE(view->rootObject()->property("desktopWheels").toInt(), 1);
    }

    void releasesButtonsWhenDesktopLosesFocus()
    {
        QTest::mousePress(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
        QTRY_COMPARE(metrics()["mousedown"].toInt(), 1);
        QEvent deactivate(QEvent::WindowDeactivate);
        QCoreApplication::sendEvent(view.get(), &deactivate);
        QTRY_COMPARE(metrics()["mouseup"].toInt(), 1);
        QCOMPARE(metrics()["buttons"].toInt(), 0);
        QCOMPARE(metrics()["buttonClicks"].toInt(), 0);
        QTest::mouseRelease(view.get(), Qt::LeftButton, Qt::NoModifier, QPoint(80, 70));
    }
};

int main(int argc, char **argv)
{
    QtWebEngineQuick::initialize();
    QGuiApplication app(argc, argv);
    MouseInputTests tests;
    return QTest::qExec(&tests, argc, argv);
}

#include "MouseInputTests.moc"
