#include "MouseInput.h"
#include <QQmlExtensionPlugin>
#include <qqml.h>

class LivePaperInputPlugin : public QQmlExtensionPlugin
{
    Q_OBJECT
    Q_PLUGIN_METADATA(IID QQmlExtensionInterface_iid)

public:
    void registerTypes(const char *uri) override
    {
        qmlRegisterType<MouseInput>(uri, 1, 0, "MouseInput");
    }
};

#include "Plugin.moc"
