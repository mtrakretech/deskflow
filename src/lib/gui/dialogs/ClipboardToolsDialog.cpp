/*
 * Deskflow -- mouse and keyboard sharing utility
 * SPDX-FileCopyrightText: (C) 2026 Deskflow Developers
 * SPDX-License-Identifier: GPL-2.0-only WITH LicenseRef-OpenSSL-Exception
 */
#include "ClipboardToolsDialog.h"

#include <QCheckBox>
#include <QDateTime>
#include <QDialogButtonBox>
#include <QFile>
#include <QFileDialog>
#include <QFormLayout>
#include <QGroupBox>
#include <QHeaderView>
#include <QInputDialog>
#include <QJsonArray>
#include <QJsonDocument>
#include <QLabel>
#include <QLineEdit>
#include <QLocale>
#include <QMessageBox>
#include <QPlainTextEdit>
#include <QProcess>
#include <QPushButton>
#include <QSaveFile>
#include <QSignalBlocker>
#include <QSplitter>
#include <QStackedWidget>
#include <QTabWidget>
#include <QTimer>
#include <QTreeWidget>
#include <QVBoxLayout>
#include <QtEndian>
#include <memory>

namespace deskflow::gui {

ClipboardToolsDialog::ClipboardToolsDialog(
    const QString &executable, bool showHistory, QWidget *parent, const QString &dataRoot
)
    : QDialog(parent),
      m_executable(executable),
      m_dataRoot(dataRoot)
{
  setAttribute(Qt::WA_DeleteOnClose);
  setWindowTitle(tr("Clipboard tools"));
  setWindowIcon(parent->windowIcon());
  resize(880, 600);
  setMinimumSize(680, 480);
  auto layout = new QVBoxLayout(this);
  m_tabs = new QTabWidget(this);
  layout->addWidget(m_tabs);

  auto history = new QWidget(m_tabs);
  auto historyLayout = new QVBoxLayout(history);
  historyLayout->setContentsMargins(12, 12, 12, 12);
  historyLayout->setSpacing(9);
  auto searchRow = new QHBoxLayout;
  auto searchLabel = new QLabel(tr("&Search:"), history);
  m_search = new QLineEdit(history);
  m_search->setObjectName(QStringLiteral("clipboardSearch"));
  m_search->setClearButtonEnabled(true);
  m_search->setPlaceholderText(tr("Search saved text, images and files"));
  searchLabel->setBuddy(m_search);
  searchRow->addWidget(searchLabel);
  searchRow->addWidget(m_search, 1);
  m_pause = new QPushButton(tr("Pause history"), history);
  searchRow->addWidget(m_pause);
  historyLayout->addLayout(searchRow);

  auto splitter = new QSplitter(history);
  m_items = new QTreeWidget(splitter);
  m_items->setHeaderLabels({tr("Content"), tr("Type"), tr("Saved")});
  m_items->setRootIsDecorated(false);
  m_items->setSelectionMode(QAbstractItemView::SingleSelection);
  m_items->setUniformRowHeights(true);
  m_items->header()->setSectionResizeMode(0, QHeaderView::Stretch);
  m_items->header()->setSectionResizeMode(1, QHeaderView::ResizeToContents);
  m_items->header()->setSectionResizeMode(2, QHeaderView::ResizeToContents);
  auto previewBox = new QGroupBox(tr("Preview"), splitter);
  auto previewLayout = new QVBoxLayout(previewBox);
  m_preview = new QStackedWidget(previewBox);
  m_text = new QPlainTextEdit(m_preview);
  m_text->setReadOnly(true);
  m_text->setPlaceholderText(tr("Your saved clipboard items will appear here."));
  m_image = new QLabel(m_preview);
  m_image->setAlignment(Qt::AlignCenter);
  m_image->setMinimumSize(100, 100);
  m_preview->addWidget(m_text);
  m_preview->addWidget(m_image);
  previewLayout->addWidget(m_preview);
  splitter->addWidget(m_items);
  splitter->addWidget(previewBox);
  splitter->setStretchFactor(0, 3);
  splitter->setStretchFactor(1, 2);
  historyLayout->addWidget(splitter, 1);

  auto actions = new QHBoxLayout;
  auto addAction = [this, history, actions](const QString &label, const QString &operation) {
    auto button = new QPushButton(label, history);
    actions->addWidget(button);
    m_selectionActions.append(button);
    button->setEnabled(false);
    connect(button, &QPushButton::clicked, this, [this, operation] { action(operation); });
  };
  addAction(tr("&Copy again"), QStringLiteral("copy"));
  addAction(tr("&Pin / unpin"), QStringLiteral("pin"));
  addAction(tr("&Delete"), QStringLiteral("delete"));
  auto save = new QPushButton(tr("Save &as…"), history);
  actions->addWidget(save);
  m_selectionActions.append(save);
  save->setEnabled(false);
  connect(save, &QPushButton::clicked, this, &ClipboardToolsDialog::saveItem);
  actions->addStretch();
  auto clear = new QPushButton(tr("Clear history…"), history);
  actions->addWidget(clear);
  historyLayout->addLayout(actions);
  auto retention = new QLabel(
      tr("Saved on this PC with Windows user encryption. Up to 100 items / 64 MiB; unpinned items expire after 7 days. "
         "File entries reference the original files."),
      history
  );
  retention->setWordWrap(true);
  historyLayout->addWidget(retention);
  m_tabs->addTab(history, tr("Clipboard &history"));

  auto sharing = new QWidget(m_tabs);
  auto sharingLayout = new QVBoxLayout(sharing);
  sharingLayout->setContentsMargins(12, 12, 12, 12);
  sharingLayout->setSpacing(9);
  auto connection = new QGroupBox(tr("Connection"), sharing);
  auto connectionLayout = new QFormLayout(connection);
  m_status = new QLabel(tr("Connecting to clipboard tools…"), connection);
  m_status->setWordWrap(true);
  m_peer = new QLabel(connection);
  m_peer->setTextInteractionFlags(Qt::TextSelectableByMouse);
  connectionLayout->addRow(tr("Status:"), m_status);
  connectionLayout->addRow(tr("Paired PC:"), m_peer);
  sharingLayout->addWidget(connection);

  auto pairing = new QGroupBox(tr("Pairing"), sharing);
  auto pairingLayout = new QVBoxLayout(pairing);
  auto instructions = new QLabel(
      tr("Import the same password-protected pairing file on your other PC, then compare both fingerprints. "
         "Both PCs must use a trusted Private network."),
      pairing
  );
  instructions->setWordWrap(true);
  pairingLayout->addWidget(instructions);
  auto fingerprintLabel = new QLabel(tr("Pair &fingerprint:"), pairing);
  pairingLayout->addWidget(fingerprintLabel);
  m_fingerprint = new QLineEdit(pairing);
  m_fingerprint->setObjectName(QStringLiteral("pairFingerprint"));
  m_fingerprint->setReadOnly(true);
  fingerprintLabel->setBuddy(m_fingerprint);
  pairingLayout->addWidget(m_fingerprint);
  auto pairActions = new QHBoxLayout;
  auto exportButton = new QPushButton(tr("Export pairing file…"), pairing);
  auto importButton = new QPushButton(tr("Import pairing file…"), pairing);
  pairActions->addWidget(exportButton);
  pairActions->addWidget(importButton);
  pairingLayout->addLayout(pairActions);
  sharingLayout->addWidget(pairing);

  auto options = new QGroupBox(tr("Setup"), sharing);
  auto optionsLayout = new QVBoxLayout(options);
  m_startup = new QCheckBox(tr("Start clipboard tools with Windows (current user)"), options);
  optionsLayout->addWidget(m_startup);
  auto setupActions = new QHBoxLayout;
  auto network = new QPushButton(tr("Configure trusted network…"), options);
  auto manual = new QPushButton(tr("Connect by IP…"), options);
  auto addPc = new QPushButton(tr("Add another PC…"), options);
  setupActions->addWidget(network);
  setupActions->addWidget(manual);
  setupActions->addWidget(addPc);
  optionsLayout->addLayout(setupActions);
  sharingLayout->addWidget(options);
  auto help = new QLabel(
      tr("Copy files in Explorer, wait for “Ready to paste” on the receiving PC, then paste. "
         "Text and screenshots share through Deskflow. Clipboard history is saved separately on each PC."),
      sharing
  );
  help->setWordWrap(true);
  sharingLayout->addWidget(help);
  sharingLayout->addStretch();
  m_tabs->addTab(sharing, tr("File sharing and &pairing"));
  m_tabs->setCurrentIndex(showHistory ? 0 : 1);

  auto footer = new QHBoxLayout;
  auto cache = new QPushButton(tr("Received files"), this);
  auto received = new QPushButton(tr("Copy received files"), this);
  auto logs = new QPushButton(tr("Logs"), this);
  footer->addWidget(cache);
  footer->addWidget(received);
  footer->addWidget(logs);
  footer->addStretch();
  auto buttons = new QDialogButtonBox(QDialogButtonBox::Close, this);
  footer->addWidget(buttons);
  layout->addLayout(footer);
  connect(buttons, &QDialogButtonBox::rejected, this, &QDialog::close);
  connect(cache, &QPushButton::clicked, this, [this] { action(QStringLiteral("cache")); });
  connect(received, &QPushButton::clicked, this, [this] { action(QStringLiteral("received")); });
  connect(logs, &QPushButton::clicked, this, [this] { action(QStringLiteral("logs")); });
  connect(m_pause, &QPushButton::clicked, this, [this] { action(QStringLiteral("pause")); });
  connect(clear, &QPushButton::clicked, this, [this] {
    if (QMessageBox::question(
            this, tr("Clear history"), tr("Delete all saved clipboard history, including pinned items?")
        ) == QMessageBox::Yes)
      action(QStringLiteral("clear"));
  });
  connect(m_search, &QLineEdit::textChanged, this, [this] { refresh(); });
  connect(m_items, &QTreeWidget::itemSelectionChanged, this, &ClipboardToolsDialog::preview);
  connect(m_items, &QTreeWidget::itemDoubleClicked, this, [this] { action(QStringLiteral("copy")); });
  connect(exportButton, &QPushButton::clicked, this, &ClipboardToolsDialog::exportPairing);
  connect(importButton, &QPushButton::clicked, this, &ClipboardToolsDialog::importPairing);
  connect(network, &QPushButton::clicked, this, [this] { action(QStringLiteral("network")); });
  connect(addPc, &QPushButton::clicked, this, [this] { action(QStringLiteral("add-pc")); });
  connect(manual, &QPushButton::clicked, this, [this] {
    bool ok;
    auto address = QInputDialog::getText(
        this, tr("Connect by IP"), tr("Other PC’s LAN IPv4 address:"), QLineEdit::Normal, {}, &ok
    );
    if (ok && !address.trimmed().isEmpty())
      request({{"op", "manual"}, {"address", address.trimmed()}}, [this](const QJsonObject &r) { showError(r); });
  });
  connect(m_startup, &QCheckBox::toggled, this, [this](bool checked) {
    request({{"op", "startup"}, {"value", checked ? "true" : "false"}}, [this](const QJsonObject &r) { showError(r); });
  });
  auto timer = new QTimer(this);
  timer->setInterval(2000);
  connect(timer, &QTimer::timeout, this, &ClipboardToolsDialog::refresh);
  timer->start();
  refresh();
}

void ClipboardToolsDialog::request(const QJsonObject &data, Reply reply)
{
  m_requests.enqueue([this, data, reply] {
    auto process = new QProcess(this);
    auto timeout = new QTimer(process);
    timeout->setSingleShot(true);
    timeout->setInterval(15000);
    auto output = std::make_shared<QByteArray>();
    auto finished = std::make_shared<bool>(false);
    auto finish = [this, process, reply, finished](const QJsonObject &response) {
      if (*finished)
        return;
      *finished = true;
      process->kill();
      process->deleteLater();
      reply(response);
      m_busy = false;
      nextRequest();
    };
    connect(timeout, &QTimer::timeout, this, [finish] {
      finish(
          {{"ok", false},
           {"error", tr("Clipboard tools did not respond. Check that the companion is running and up to date.")}}
      );
    });
    connect(process, &QProcess::errorOccurred, this, [finish](QProcess::ProcessError error) {
      if (error == QProcess::FailedToStart)
        finish({{"ok", false}, {"error", tr("Could not start clipboard tools.")}});
    });
    connect(process, &QProcess::readyReadStandardOutput, this, [process, output, finish] {
      output->append(process->readAllStandardOutput());
      if (output->size() > 32 * 1024 * 1024 + 4)
        finish({{"ok", false}, {"error", tr("Clipboard tools response is too large.")}});
    });
    connect(process, qOverload<int, QProcess::ExitStatus>(&QProcess::finished), this, [process, output, finish] {
      output->append(process->readAllStandardOutput());
      if (output->size() < 4 || qFromLittleEndian<quint32>(output->constData()) != quint32(output->size() - 4)) {
        finish({{"ok", false}, {"error", tr("Update the clipboard companion to use this interface.")}});
        return;
      }
      auto response = QJsonDocument::fromJson(output->mid(4)).object();
      if (!response.contains("ok"))
        response = {{"ok", false}, {"error", tr("Invalid clipboard tools response.")}};
      finish(response);
    });
    connect(process, &QProcess::started, this, [process, data] {
      auto json = QJsonDocument(data).toJson(QJsonDocument::Compact);
      QByteArray framed(4, Qt::Uninitialized);
      qToLittleEndian<quint32>(quint32(json.size()), framed.data());
      framed.append(json);
      process->write(framed);
      process->closeWriteChannel();
    });
    QStringList arguments{QStringLiteral("--ui")};
    if (!m_dataRoot.isEmpty())
      arguments.append({QStringLiteral("--data-root"), m_dataRoot});
    process->start(m_executable, arguments);
    timeout->start();
  });
  nextRequest();
}

void ClipboardToolsDialog::nextRequest()
{
  if (m_busy || m_requests.isEmpty())
    return;
  m_busy = true;
  m_requests.dequeue()();
}

void ClipboardToolsDialog::showError(const QJsonObject &reply)
{
  if (!reply.value("ok").toBool())
    QMessageBox::warning(this, tr("Clipboard tools"), reply.value("error").toString());
}

void ClipboardToolsDialog::refresh()
{
  if (m_refreshing || m_busy)
    return;
  m_refreshing = true;
  request({{"op", "status"}}, [this](const QJsonObject &r) {
    if (!r.value("ok").toBool()) {
      m_status->setText(r.value("error").toString());
      m_text->setPlaceholderText(r.value("error").toString());
      m_refreshing = false;
      return;
    }
    m_status->setText(r.value("status").toString());
    m_peer->setText(r.value("peer").toString());
    m_fingerprint->setText(r.value("fingerprint").toString());
    const QSignalBlocker blocker(m_startup);
    m_startup->setChecked(r.value("startup").toBool());
    m_pause->setText(r.value("paused").toBool() ? tr("Resume history") : tr("Pause history"));
    request({{"op", "list"}, {"search", m_search->text()}}, [this](const QJsonObject &items) {
      if (items.value("ok").toBool())
        updateItems(items);
      m_refreshing = false;
    });
  });
}

QString ClipboardToolsDialog::selectedId() const
{
  auto item = m_items->currentItem();
  return item ? item->data(0, Qt::UserRole).toString() : QString();
}

void ClipboardToolsDialog::updateItems(const QJsonObject &reply)
{
  auto entries = reply.value("items").toArray();
  auto signature = QJsonDocument(entries).toJson(QJsonDocument::Compact);
  if (signature == m_listing)
    return;
  m_listing = signature;
  auto selected = selectedId();
  {
    const QSignalBlocker blocker(m_items);
    m_items->clear();
    for (const auto &entry : entries) {
      auto i = entry.toObject();
      auto summary = i.value("preview").toString();
      if (i.value("pinned").toBool())
        summary = tr("[Pinned] %1").arg(summary);
      auto created = QDateTime::fromString(i.value("created").toString(), Qt::ISODateWithMs).toLocalTime();
      auto row = new QTreeWidgetItem(
          m_items, {summary, i.value("kind").toString(), QLocale().toString(created, QLocale::ShortFormat)}
      );
      row->setData(0, Qt::UserRole, i.value("id").toString());
      row->setToolTip(0, summary);
      if (i.value("id").toString() == selected)
        m_items->setCurrentItem(row);
    }
    if (!m_items->currentItem() && m_items->topLevelItemCount())
      m_items->setCurrentItem(m_items->topLevelItem(0));
  }
  preview();
}

void ClipboardToolsDialog::preview()
{
  auto id = selectedId();
  for (auto button : m_selectionActions)
    button->setEnabled(!id.isEmpty());
  if (id == m_previewId)
    return;
  m_previewId = id;
  m_previewData = {};
  m_text->clear();
  m_image->clear();
  m_preview->setCurrentWidget(m_text);
  if (id.isEmpty())
    return;
  request({{"op", "preview"}, {"id", id}}, [this, id](const QJsonObject &r) {
    if (selectedId() != id)
      return;
    if (!r.value("ok").toBool()) {
      m_text->setPlainText(r.value("error").toString());
      return;
    }
    m_previewData = r;
    auto png = QByteArray::fromBase64(r.value("png").toString().toLatin1());
    if (!png.isEmpty()) {
      QPixmap image;
      image.loadFromData(png, "PNG");
      m_image->setPixmap(image.scaled(m_preview->size(), Qt::KeepAspectRatio, Qt::SmoothTransformation));
      m_preview->setCurrentWidget(m_image);
    } else {
      m_text->setPlainText(r.value("text").toString());
      m_preview->setCurrentWidget(m_text);
    }
  });
}

void ClipboardToolsDialog::action(const QString &operation)
{
  request({{"op", operation}, {"id", selectedId()}}, [this, operation](const QJsonObject &r) {
    showError(r);
    if (r.value("ok").toBool() && operation == QStringLiteral("copy"))
      close();
  });
}

void ClipboardToolsDialog::saveItem()
{
  if (!m_previewData.value("ok").toBool())
    return;
  if (m_previewData.value("files").toBool()) {
    QMessageBox::information(
        this, tr("Save clipboard item"), tr("Use Copy again, then paste the referenced files in Explorer.")
    );
    return;
  }
  bool image = !m_previewData.value("png").toString().isEmpty();
  auto path = QFileDialog::getSaveFileName(
      this, tr("Save clipboard item"), image ? "clipboard.png" : "clipboard.txt",
      image ? tr("PNG images (*.png)") : tr("Text files (*.txt)")
  );
  if (path.isEmpty())
    return;
  QSaveFile file(path);
  auto data = image ? QByteArray::fromBase64(m_previewData.value("png").toString().toLatin1())
                    : m_previewData.value("text").toString().toUtf8();
  if (!file.open(QIODevice::WriteOnly) || file.write(data) != data.size() || !file.commit())
    QMessageBox::warning(this, tr("Save clipboard item"), file.errorString());
}

void ClipboardToolsDialog::exportPairing()
{
  auto path = QFileDialog::getSaveFileName(
      this, tr("Export pairing file"), "Viola-pairing.vfbpair", tr("Pairing files (*.vfbpair)")
  );
  if (path.isEmpty())
    return;
  bool ok;
  auto password = QInputDialog::getText(
      this, tr("Export pairing file"), tr("Choose a password of at least 12 characters. Share it separately."),
      QLineEdit::Password, {}, &ok
  );
  if (!ok)
    return;
  request({{"op", "pair-export"}, {"password", password}}, [this, path](const QJsonObject &r) {
    if (!r.value("ok").toBool()) {
      showError(r);
      return;
    }
    QSaveFile file(path);
    auto bytes = QByteArray::fromBase64(r.value("data").toString().toLatin1());
    if (!file.open(QIODevice::WriteOnly) || file.write(bytes) != bytes.size() || !file.commit()) {
      QMessageBox::warning(this, tr("Export pairing file"), file.errorString());
      return;
    }
    QMessageBox::information(
        this, tr("Pairing file exported"),
        tr("Import this file on the other PC and compare fingerprints. Delete the exported file after pairing.")
    );
  });
}

void ClipboardToolsDialog::importPairing()
{
  auto path = QFileDialog::getOpenFileName(this, tr("Import pairing file"), {}, tr("Pairing files (*.vfbpair)"));
  if (path.isEmpty())
    return;
  QFile file(path);
  if (!file.open(QIODevice::ReadOnly) || file.size() > 65536) {
    QMessageBox::warning(this, tr("Import pairing file"), tr("Could not read the pairing file, or it exceeds 64 KiB."));
    return;
  }
  auto data = QString::fromLatin1(file.readAll().toBase64());
  bool ok;
  auto password = QInputDialog::getText(
      this, tr("Import pairing file"), tr("Pairing file password:"), QLineEdit::Password, {}, &ok
  );
  if (!ok)
    return;
  request(
      {{"op", "pair-inspect"}, {"data", data}, {"password", password}},
      [this, data, password](const QJsonObject &r) {
        if (!r.value("ok").toBool()) {
          showError(r);
          return;
        }
        auto pin = r.value("fingerprint").toString();
        if (QMessageBox::question(
                this, tr("Confirm pairing"),
                tr("Replace the current pairing with this fingerprint?\n\n%1\n\nCompare it with the other PC.").arg(pin)
            ) != QMessageBox::Yes)
          return;
        request(
            {{"op", "pair-import"}, {"data", data}, {"password", password}, {"confirmedFingerprint", pin}},
            [this](const QJsonObject &reply) { showError(reply); }
        );
      }
  );
}
} // namespace deskflow::gui
