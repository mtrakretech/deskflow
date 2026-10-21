/*
 * SPDX-FileCopyrightText: (C) 2026 Deskflow Developers
 * SPDX-License-Identifier: GPL-2.0-only WITH LicenseRef-OpenSSL-Exception
 */
#include "dialogs/ClipboardToolsDialog.h"

#include <QApplication>
#include <QDir>
#include <QElapsedTimer>
#include <QFontDatabase>
#include <QLabel>
#include <QLineEdit>
#include <QPlainTextEdit>
#include <QProcess>
#include <QTabWidget>
#include <QTemporaryDir>
#include <QTimer>
#include <QTreeWidget>
#include <iostream>

int main(int argc, char **argv)
{
  QApplication app(argc, argv);
  // Match the Windows Deskflow entry point. The offscreen plugin needs an
  // explicit font because it does not load the Windows platform font database.
  QApplication::setStyle(QStringLiteral("fusion"));
  if (QFontDatabase::addApplicationFont(QDir(qEnvironmentVariable("WINDIR")).filePath("Fonts/segoeui.ttf")) < 0)
    return 9;
  QApplication::setFont(QFont(QStringLiteral("Segoe UI"), 9));
  if (argc != 4)
    return 2;
  QTemporaryDir root;
  if (!root.isValid())
    return 3;
  QProcess backend;
  backend.start(QString::fromLocal8Bit(argv[1]), {QStringLiteral("--ui-fixture"), root.path()});
  if (!backend.waitForStarted())
    return 4;
  QWidget parent;
  auto dialog = new deskflow::gui::ClipboardToolsDialog(QString::fromLocal8Bit(argv[2]), true, &parent, root.path());
  dialog->show();
  auto items = dialog->findChild<QTreeWidget *>();
  auto search = dialog->findChild<QLineEdit *>(QStringLiteral("clipboardSearch"));
  auto text = dialog->findChild<QPlainTextEdit *>();
  auto tabs = dialog->findChild<QTabWidget *>();
  auto pin = dialog->findChild<QLineEdit *>(QStringLiteral("pairFingerprint"));
  QString output = QString::fromLocal8Bit(argv[3]);
  QDir().mkpath(output);
  QElapsedTimer elapsed;
  elapsed.start();
  int stage = 0;
  QTimer poll;
  poll.setInterval(100);
  QObject::connect(&poll, &QTimer::timeout, &app, [&] {
    if (elapsed.elapsed() > 45000) {
      std::cerr << "FAIL Qt dialog smoke stage " << stage << std::endl;
      app.exit(1);
      return;
    }
    if (stage == 0 && items->topLevelItemCount() == 3) {
      if (dialog->style() != app.style() || dialog->palette() != app.palette()) {
        app.exit(5);
        return;
      }
      for (int i = 0; i < items->topLevelItemCount(); ++i)
        if (items->topLevelItem(i)->text(0).startsWith("Welcome"))
          items->setCurrentItem(items->topLevelItem(i));
      stage = 1;
    } else if (stage == 1 && text->toPlainText().contains("Find it here later")) {
      if (!dialog->grab().save(output + "/clipboard-history.png")) {
        app.exit(6);
        return;
      }
      search->setText("no-such-fixture-item");
      stage = 2;
    } else if (stage == 2 && items->topLevelItemCount() == 0 && text->toPlainText().isEmpty()) {
      search->clear();
      stage = 3;
    } else if (stage == 3 && items->topLevelItemCount() == 3) {
      for (int i = 0; i < items->topLevelItemCount(); ++i)
        if (items->topLevelItem(i)->text(1).contains("image"))
          items->setCurrentItem(items->topLevelItem(i));
      stage = 4;
    } else if (stage == 4) {
      bool imageReady = false;
      for (auto label : dialog->findChildren<QLabel *>())
        imageReady |= !label->pixmap().isNull();
      if (!imageReady)
        return;
      if (!dialog->grab().save(output + "/clipboard-image.png")) {
        app.exit(7);
        return;
      }
      tabs->setCurrentIndex(1);
      stage = 5;
    } else if (stage == 5 && pin->text().size() == 64) {
      if (!dialog->grab().save(output + "/clipboard-pairing.png")) {
        app.exit(8);
        return;
      }
      std::cout << "PASS Qt style inheritance, history list, search, text/image previews and pairing status"
                << std::endl;
      app.exit(0);
    }
  });
  poll.start();
  int result = app.exec();
  backend.terminate();
  if (!backend.waitForFinished(2000)) {
    backend.kill();
    backend.waitForFinished(2000);
  }
  return result;
}
