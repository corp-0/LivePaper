#include "MouseInput.h"
#include "InputVersion.h"

#include <QCoreApplication>
#include <QMouseEvent>
#include <QScopedValueRollback>
#include <QWheelEvent>

namespace
{
bool isMouseInput(QEvent::Type type)
{
    return type == QEvent::MouseButtonPress || type == QEvent::MouseButtonRelease
        || type == QEvent::MouseButtonDblClick || type == QEvent::MouseMove
        || type == QEvent::Wheel;
}

QQuickItem *findInputItem(QQuickItem *item)
{
    // WebEngine receives mouse input through a child Quick item.
    for (auto *child : item->childItems())
    {
        if (auto *receiver = findInputItem(child))
        {
            return receiver;
        }
    }
    return item->acceptedMouseButtons() != Qt::NoButton ? item : nullptr;
}
}

MouseInput::MouseInput(QQuickItem *parent) : QQuickItem(parent)
{
    connect(this, &QQuickItem::windowChanged, this, &MouseInput::watchWindow);
    connect(this, &QQuickItem::enabledChanged, this, [this]() {
        if (!isEnabled())
        {
            releaseButtons();
        }
    });
    watchWindow(window());
}

QString MouseInput::buildVersion() const
{
    return QStringLiteral(LIVEPAPER_INPUT_VERSION);
}

MouseInput::~MouseInput()
{
    watchWindow(nullptr);
    if (m_receiver)
    {
        m_receiver->removeEventFilter(this);
    }
}

void MouseInput::watchWindow(QQuickWindow *window)
{
    releaseButtons();
    if (m_window)
    {
        m_window->removeEventFilter(this);
    }
    m_window = window;
    if (m_window)
    {
        m_window->installEventFilter(this);
    }
}

QQuickItem *MouseInput::inputItem()
{
    auto *view = parentItem();
    if (!view)
    {
        return nullptr;
    }
    if (m_receiver && view->isAncestorOf(m_receiver))
    {
        return m_receiver;
    }
    if (m_receiver)
    {
        m_receiver->removeEventFilter(this);
    }
    m_receiver = findInputItem(view);
    if (m_receiver)
    {
        m_receiver->installEventFilter(this);
    }
    return m_receiver;
}

void MouseInput::releaseButtons()
{
    if (m_receiver && m_buttons != Qt::NoButton)
    {
        QScopedValueRollback forwarding(m_forwarding, true);
        const QPointF position(-1, -1);
        QMouseEvent move(QEvent::MouseMove, position,
            m_receiver->mapToScene(position), m_receiver->mapToGlobal(position),
            Qt::NoButton, m_buttons, Qt::NoModifier);
        QCoreApplication::sendEvent(m_receiver, &move);
        for (auto button : {Qt::LeftButton, Qt::RightButton, Qt::MiddleButton, Qt::BackButton, Qt::ForwardButton})
        {
            if (!m_buttons.testFlag(button))
            {
                continue;
            }
            m_buttons &= ~button;
            // Release outside the pressed control when the desktop loses focus.
            QMouseEvent release(QEvent::MouseButtonRelease, position,
                m_receiver->mapToScene(position), m_receiver->mapToGlobal(position),
                button, m_buttons, Qt::NoModifier);
            QCoreApplication::sendEvent(m_receiver, &release);
        }
    }
    m_buttons = Qt::NoButton;
}

bool MouseInput::eventFilter(QObject *watched, QEvent *event)
{
    if (m_forwarding)
    {
        return false;
    }
    if (watched == m_window && (event->type() == QEvent::WindowDeactivate || event->type() == QEvent::Hide))
    {
        releaseButtons();
    }
    if (!isEnabled() || !isVisible() || !isMouseInput(event->type()))
    {
        return false;
    }
    if (watched == m_receiver)
    {
        // The window copy already reached WebEngine, even without a desktop overlay.
        return true;
    }
    if (watched != m_window)
    {
        return false;
    }
    auto *receiver = inputItem();
    auto *view = parentItem();
    if (!receiver || !view->isVisible() || !view->isEnabled())
    {
        return false;
    }
    auto *pointer = static_cast<QSinglePointEvent *>(event);
    const auto scenePosition = pointer->position();
    if (!view->contains(view->mapFromScene(scenePosition)) && m_buttons == Qt::NoButton)
    {
        return false;
    }
    const auto localPosition = receiver->mapFromScene(scenePosition);
    QScopedValueRollback forwarding(m_forwarding, true);
    if (event->type() == QEvent::Wheel)
    {
        auto *wheel = static_cast<QWheelEvent *>(event);
        QWheelEvent copy(localPosition, wheel->globalPosition(), wheel->pixelDelta(), wheel->angleDelta(),
            wheel->buttons(), wheel->modifiers(), wheel->phase(), wheel->inverted(), wheel->source(), wheel->pointingDevice());
        copy.setTimestamp(wheel->timestamp());
        QCoreApplication::sendEvent(receiver, &copy);
    }
    else
    {
        auto *mouse = static_cast<QMouseEvent *>(event);
        if (event->type() == QEvent::MouseButtonRelease)
        {
            if (!m_buttons.testFlag(mouse->button()))
            {
                return false;
            }
            m_buttons &= ~mouse->button();
        }
        else if (event->type() == QEvent::MouseButtonPress || event->type() == QEvent::MouseButtonDblClick)
        {
            m_buttons |= mouse->button();
        }
        QMouseEvent copy(mouse->type(), localPosition, scenePosition, mouse->globalPosition(),
            mouse->button(), mouse->buttons(), mouse->modifiers(), mouse->pointingDevice());
        copy.setTimestamp(mouse->timestamp());
        QCoreApplication::sendEvent(receiver, &copy);
    }
    // Plasma still receives the original event for icons, selection and desktop actions.
    return false;
}
