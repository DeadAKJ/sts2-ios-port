# Slay the Spire 2 - iOS Port & AltStore Sideload Guide

This repository contains the complete iOS port project and automated GitHub Actions build pipeline for **Slay the Spire 2** (Godot 4.5.1 / .NET 9).

---

## Features & Adaptations in this Port

- **Touch Controls:** Custom touch handling and card drag-and-drop mechanics (`TouchInputPatches.cs`).
- **UI Scaling:** Custom responsive UI scaling for iPhone screens (`UiScalePatches.cs`) accessible directly via in-game Settings.
- **Steamworks Bypassed:** Native iOS C stubs (`steam_stub.c`) replace `steam_api64.dll`, allowing offline play without DRM crashes.
- **Sentry Disabled:** Crash telemetry disabled to avoid unnecessary overhead and background network calls.
- **iOS Sandbox & File Sharing:** `UIFileSharingEnabled` and `LSSupportsOpeningDocumentsInPlace` are enabled. You can manage your saves and mods directly through the **Files** app on iOS (`On My iPhone -> Slay the Spire 2`).

---

## Step 1: Create a Private GitHub Repository

1. Go to [GitHub.com](https://github.com/new) and create a **Private** repository (e.g. `sts2-ios-port`).
   > ⚠️ **Important:** Ensure the repository is **Private** to protect your game assemblies and personal configuration.

2. In PowerShell, initialize git and push to your new repository:
   ```powershell
   cd c:\Users\User\Documents\ANTI\STS2port
   git init
   git add .
   git commit -m "Initial commit of STS2 iOS port"
   git branch -M main
   git remote add origin https://github.com/YOUR_USERNAME/sts2-ios-port.git
   git push -u origin main
   ```

---

## Step 2: Choose How to Supply Game Assets (`SlayTheSpire2.pck`)

The game assets (`SlayTheSpire2.pck`, ~1.9 GB) can be provided in either of two ways:

### Option A: Via the iOS Files App / iTunes (Recommended - Fastest)
1. Push the repository as-is without uploading the 1.9 GB PCK to GitHub.
2. The GitHub Action will build a lightweight runner `.ipa` (~50 MB).
3. Sideload the `.ipa` using AltStore.
4. On your iPhone, open the **Files** app $\rightarrow$ **On My iPhone** $\rightarrow$ **Slay the Spire 2**.
5. Copy `SlayTheSpire2.pck` from your PC (`E:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck`) into that folder (using AirDrop, iTunes/Finder File Sharing, iCloud Drive, or LocalSend).
6. Launch the game!

### Option B: Bundle Assets Directly into the IPA (All-in-One)
1. Before pushing to GitHub, run our PCK splitter script:
   ```powershell
   python scripts\split_pck.py
   ```
   This splits `SlayTheSpire2.pck` into 90 MB chunks inside `pck_parts/` (under GitHub's 100 MB per-file limit).
2. Commit and push the parts:
   ```powershell
   git add pck_parts
   git commit -m "Add split game assets"
   git push
   ```
3. GitHub Actions will reassemble the parts during the build and produce a self-contained IPA containing all audio and art assets.

---

## Step 3: Download the IPA from GitHub Actions

1. In your GitHub repository, click on the **Actions** tab.
2. Select the latest run of the **Build iOS IPA** workflow.
3. Once finished, scroll down to the **Artifacts** section at the bottom.
4. Download `SlayTheSpire2-iOS-ipa.zip` and extract `SlayTheSpire2-iOS.ipa`.

---

## Step 4: Sideload via AltStore

1. Send `SlayTheSpire2-iOS.ipa` to your iPhone (via AirDrop, iCloud Drive, or cable).
2. Open **AltStore** on your iPhone.
3. Tap the **My Apps** tab $\rightarrow$ tap the **$+$** button in the top-left corner.
4. Select `SlayTheSpire2-iOS.ipa`.
5. AltStore will sign the app with your Apple ID and install it to your home screen!
