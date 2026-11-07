# 🛡️ Startup Monitor User Guide

[Русская версия](USER_GUIDE.ru.md) | [Main Project Page](../README.md)

Comprehensive practical manual on installation, configuration, cloud integrations, and daily usage of all modules and features in the **Startup Monitor** platform.

---

## 📑 Table of Contents

1. [Introduction & System Requirements](#1-introduction--system-requirements)
2. [Module & Feature Overview](#2-module--feature-overview)
   - [Overview (Dashboard)](#tab-1-overview-dashboard)
   - [Startup Entries](#tab-2-startup-entries)
   - [Task Manager & Process Tree](#tab-3-task-manager--process-tree)
   - [Boot Time Tracer](#tab-4-boot-time-tracer)
   - [Blacklist & Auto-Guards](#tab-5-blacklist--auto-guards)
   - [Recycle Bin (Removed Items)](#tab-6-recycle-bin-removed-items)
   - [Snapshots (State Diffs)](#tab-7-snapshots-before--after)
   - [Audit Logs](#tab-8-audit-logs)
   - [Network Security Hub](#tab-9-network-security-hub)
   - [Settings & Customization](#tab-10-settings--customization)
3. [Operation Modes: Disable vs. Block vs. Remove](#3-operation-modes-disable-vs-block-vs-remove)
4. [External Integrations & API Keys](#4-external-integrations--api-keys)
   - [Cloud AI Heuristics (Groq Cloud)](#41-cloud-ai-heuristics-groq-cloud)
   - [Local Offline Models (Ollama / LM Studio)](#42-local-offline-ai-models-ollama--lm-studio)
   - [Kaspersky OpenTIP Threat Intel](#43-kaspersky-opentip-threat-intelligence)
   - [Mobile Notifications via Telegram](#44-remote-telegram-push-notifications)
   - [Discord Webhook Alerts](#45-discord-webhook-alerts)
5. [Frequently Asked Questions (FAQ)](#5-frequently-asked-questions-faq)

---

## 1. Introduction & System Requirements

**Startup Monitor** is an enterprise-grade Windows security and optimization utility combining startup entry auditing, zero-overhead kernel process tracing via ETW, dynamic Windows Filtering Platform (WFP) firewall rules, and proactive network perimeter defense.

### Minimum System Requirements
- **Operating System**: Windows 10 (version 1809+, 64-bit) or Windows 11 (all editions).
- **Architecture**: x64 (AMD64 / Intel 64).
- **Runtime Environment**: Bundled self-contained .NET 8 (no external runtimes or frameworks required).
- **Permissions**: Administrative privileges for service registration and WFP firewall driver manipulation.

---

## 2. Module & Feature Overview

### Tab 1: Overview (Dashboard)
- **Central KPI Dashboard**: Displays total tracked startup points, risk category distribution (Clean / Review / Suspicious / Dangerous), and background Windows Service health (`NT AUTHORITY\SYSTEM`).
- **"New in 24 Hours" Filter**: Single-click toggle to isolate all items registered within the last 24 hours (vital immediately after installing software updates or third-party apps).
- **Global Pause (Pause / Resume)**: Instantly halts all registry hooks, folder watchers, and WMI event subscriptions during benchmark or maintenance periods without polluting logs.

### Tab 2: Startup Entries
Monitors over 29 persistence locations across Windows:
1. `HKLM/HKCU ... CurrentVersion\Run` and `RunOnce`.
2. Group Policies: `Policies\Explorer\Run`.
3. Startup folders: User `Shell:Startup` and System `Common Startup`.
4. Windows Services (SCM) in Automatic and Delayed-Start modes.
5. Windows Task Scheduler jobs (Triggers: Logon, Boot, Event-driven).
6. WMI Event Subscriptions (`root\subscription`: `CommandLineEventConsumer`).
7. Core Winlogon hooks (Userinit, Shell, Notify).
8. Explorer Shell Extensions: ContextMenuHandlers, BHOs, AppPaths, IFEO (Image File Execution Options), AppInit_DLLs.

#### Interactive Controls:
- **Enable/Disable Switch**: Temporarily deactivates auto-start without deleting the executable. Shortcuts receive `.disabled` extensions, services switch to `Disabled`, and registry entries move to a shadow quarantine vault. Reversible in one click.
- **Block Button**: Permanently prevents startup, creates a persistent blacklist rule, and safely archives parameters in `DisablePayload`.
- **Context Menu (Right-Click)**:
  - *Details & AI Analysis*: Comprehensive diagnostic sheet with Groq/Ollama explanation, Kaspersky threat classification, PE headers, and Authenticode validity.
  - *Delayed Startup*: Postpones execution by 15, 30, 60, or 120 seconds post-login to eliminate boot-time CPU contention.
  - *Open in Explorer / Open in Registry*: Instant navigation to physical path or `regedit` branch.

### Tab 3: Task Manager & Process Tree
- **60 FPS Virtualized Process Tree**: Hierarchy reconstruction via `Parent Process ID (PPID)`.
- **Real-Time Telemetry**: Instant CPU %, Private Working Set (RAM), disk I/O throughput, and aggregate network usage.
- **Process Freezing (Suspend / Resume)**: Freezes all threads via native NT kernel calls (`NtSuspendProcess` / `NtResumeProcess`) without terminating the task, releasing 100% CPU capacity.
- **Socket Inspector**: Audits active TCP/UDP sockets, remote endpoints, and ports with deep links to Whois (IPInfo.io) and VirusTotal.

### Tab 4: Boot Time Tracer
- Analyzes Windows startup velocity via official `Microsoft-Windows-Diagnostics-Performance` event stream (Event IDs 100–110).
- Pinpoints latency offenders with micro-badges (e.g., `⏳ +4.2s`).
- One-click transfer of heavy boot items into the Delayed Startup queue.

### Tab 5: Blacklist & Auto-Guards
- Proactive defense against crypto-miners, unwanted updaters, and unauthorized scripts.
- **Auto-Guards Policies**:
  - Block executions from `%TEMP%` and `%APPDATA%\Local\Temp`.
  - Block untrusted script engines (`.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `mshta`).
  - Block binaries missing valid Authenticode digital signatures.

### Tab 6: Recycle Bin (Removed Items)
- Safe backup repository for deleted startup entries.
- Click **Restore** to recreate original registry keys, task triggers, or services with zero parameter loss.

### Tab 7: Snapshots ("Before & After")
- Create reference baselines prior to running third-party installers.
- Differential state engine highlights added (`+`), modified (`~`), and deleted (`-`) keys.

### Tab 8: Audit Logs
- Append-only event history with severity filtering (Info, Warning, Block, Error).
- One-click export to interactive standalone Cyber/Fluent HTML reports, CSV (Excel UTF-8 BOM), or SIEM-ready JSON.

### Tab 9: Network Security Hub
- **Windows Firewall Control**: Inspect and toggle Inbound/Outbound rules across Domain, Private, and Public profiles.
- **Port Defender (Fail2ban for Windows)**: Safeguards exposed endpoints (RDP: 3389, SSH: 22, SMB: 445). Detects brute-force floods and creates transient WFP block rules while preserving local RFC 1918 subnets.
- **Connection Alerts & GeoIP**: Instant desktop toast flyouts upon outbound socket activity with country flag, city, and ISP attribution.
- **Silent Mode for Kernel Sockets**: Suppresses notifications for internal system sockets (PID = 0).

### Tab 10: Settings & Customization
- Seamless UI theme switching (Dark, Light, Mica / Acrylic).
- Realtime UI language hot-swapping (English / Russian).
- Credentials vault for external API tokens and push channels.

---

## 3. Operation Modes: Disable vs. Block vs. Remove

| Feature | 1. Disable | 2. Block | 3. Remove |
| :--- | :--- | :--- | :--- |
| **Primary Intent** | Temporary deactivation. Target program remains functional. | Permanent prohibition of startup and reappearance. | Full purge with safety backup creation. |
| **Trigger Action** | Status switch in table or Right-Click → Disable. | "Block" button or Blacklist tab. | Right-Click → "Remove from System". |
| **OS Mechanism** | Shortcut renamed to `*.disabled`, service set to `Disabled`, key moved to shadow vault. | Entry deactivated, rule written to `blacklist.json`, payload preserved. | Physical deletion of registry key, file shortcut, or task. |
| **Process State** | Process **NOT terminated**. | Process terminated (unless core OS kernel process). | Process terminated prior to entry deletion. |
| **Persistence Defense** | Service silently neutralizes regenerated keys without closing application. | Watchers actively suppress recurrence and enforce block. | Program can recreate key unless blacklisted. |
| **Recovery** | Flip switch back to Enabled. | Remove rule from Blacklist tab and enable entry. | Open Recycle Bin tab → Click "Restore". |

---

## 4. External Integrations & API Keys

Startup Monitor runs fully standalone without any internet access. Connect the following optional services for enhanced threat intelligence:

### 4.1. Cloud AI Heuristics (Groq Cloud)
Fast, low-latency AI audits via OpenAI-compatible endpoints:
1. Sign up at [console.groq.com](https://console.groq.com/).
2. Navigate to **API Keys** ([console.groq.com/keys](https://console.groq.com/keys)) and click **Create API Key**.
3. Copy the token starting with `gsk_...`.
4. Open **Settings → AI Rating** in Startup Monitor:
   - Paste the token into the API Key field;
   - Default Endpoint: `https://api.groq.com/openai/v1/chat/completions`;
   - Model: `openai/gpt-oss-120b` or `llama-3.3-70b-versatile`;
   - Click **Verify Connection**.

### 4.2. Local Offline AI Models (Ollama / LM Studio)
For strictly offline, air-gapped, or privacy-sensitive workstations:
1. Install [Ollama](https://ollama.ai/) and pull a lightweight model:
   ```bash
   ollama run llama3.2
   ```
2. In Startup Monitor settings, set Endpoint to `http://localhost:11434/v1/chat/completions`, model to `llama3.2`, and leave the API key empty.

### 4.3. Kaspersky OpenTIP Threat Intelligence
1. Register a free account at [opentip.kaspersky.com](https://opentip.kaspersky.com/).
2. Generate an API token under **API Management** ([opentip.kaspersky.com/token](https://opentip.kaspersky.com/token)).
3. Insert token into Startup Monitor and click **Verify Token**.

### 4.4. Remote Telegram Push Notifications
1. Contact [@BotFather](https://t.me/BotFather) on Telegram, execute `/newbot`, and save the API token.
2. Send `/start` to your newly created bot.
3. Retrieve your numerical user ID via [@userinfobot](https://t.me/userinfobot).
4. Enable Telegram notifications in Startup Monitor, enter token and Chat ID, and click **Send Test Notification**.

### 4.5. Discord Webhook Alerts
1. In Discord channel settings, go to **Integrations → Webhooks → New Webhook**.
2. Copy the Webhook URL.
3. Paste into Startup Monitor settings and test.

---

## 5. Frequently Asked Questions (FAQ)

**Q: Does the background service increase CPU consumption when monitoring continuously?**  
A: No. The service relies entirely on asynchronous kernel change notifications (`RegNotifyChangeKeyValue`, `FileSystemWatcher`, WMI subscriptions, and ETW traces). Under normal conditions, idle CPU usage is **0%**.

**Q: What happens if I attempt to block a Windows system component?**  
A: Startup Monitor includes a hardcoded kernel whitelist (`svchost.exe`, `lsass.exe`, `csrss.exe`, `services.exe`, etc.). Any destructive action against protected operating system processes is rejected.

**Q: Where are settings and safety backups stored?**  
A: All persistent files (`settings.json`, `blacklist.json`, `startup-entries.json`, `removed-entries.json`) reside in the program directory with automated `.backup` rotation and write-ahead verification.
