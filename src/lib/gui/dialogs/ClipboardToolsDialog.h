/*
 * Deskflow -- mouse and keyboard sharing utility
 * SPDX-FileCopyrightText: (C) 2026 Deskflow Developers
 * SPDX-License-Identifier: GPL-2.0-only WITH LicenseRef-OpenSSL-Exception
 */
#pragma once

#include <QDialog>
#include <QJsonObject>
#include <QQueue>
#include <functional>

class QCheckBox;
class QLabel;
class QLineEdit;
class QPlainTextEdit;
class QPushButton;
class QStackedWidget;
class QTabWidget;
class QTreeWidget;

namespace deskflow::gui {

// Uses the application's existing Qt style and palette. The Windows companion
// owns capture, encrypted storage and transfers; the GUI never reads its keys.
class ClipboardToolsDialog : public QDialog
{
  Q_OBJECT
public:
  ClipboardToolsDialog(const QString &executable, bool showHistory, QWidget *parent, const QString &dataRoot = {});

private:
  using Reply = std::function<void(const QJsonObject &)>;
  void request(const QJsonObject &data, Reply reply);
  void nextRequest();
  void refresh();
  void updateItems(const QJsonObject &reply);
  void preview();
  void action(const QString &operation);
  void exportPairing();
  void importPairing();
  void saveItem();
  QString selectedId() const;
  void showError(const QJsonObject &reply);

  QString m_executable;
  QString m_dataRoot;
  QQueue<std::function<void()>> m_requests;
  bool m_busy = false;
  bool m_refreshing = false;
  QByteArray m_listing;
  QTabWidget *m_tabs;
  QTreeWidget *m_items;
  QLineEdit *m_search;
  QPlainTextEdit *m_text;
  QLabel *m_image;
  QStackedWidget *m_preview;
  QLabel *m_status;
  QLabel *m_peer;
  QLineEdit *m_fingerprint;
  QCheckBox *m_startup;
  QPushButton *m_pause;
  QList<QPushButton *> m_selectionActions;
  QString m_previewId;
  QJsonObject m_previewData;
};
} // namespace deskflow::gui
