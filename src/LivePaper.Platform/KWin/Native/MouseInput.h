#pragma once

#include <QPointer>
#include <QQuickItem>
#include <QQuickWindow>

class MouseInput : public QQuickItem
{
    Q_OBJECT
    Q_PROPERTY(QString buildVersion READ buildVersion CONSTANT)

public:
    explicit MouseInput(QQuickItem *parent = nullptr);
    ~MouseInput() override;
    QString buildVersion() const;

protected:
    bool eventFilter(QObject *watched, QEvent *event) override;

private:
    void watchWindow(QQuickWindow *window);
    void releaseButtons();
    QQuickItem *inputItem();

    QPointer<QQuickWindow> m_window;
    QPointer<QQuickItem> m_receiver;
    Qt::MouseButtons m_buttons = Qt::NoButton;
    bool m_forwarding = false;
};
