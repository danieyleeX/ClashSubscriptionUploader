# Clash 节点订阅工具

Windows Forms desktop app that fetches OpenRung's signed Broker candidate list, converts compatible direct VLESS + REALITY entries to Mihomo YAML, previews the result, and uploads it to FTP/explicit FTPS.

## Build

Open `ClashSubscriptionUploader.csproj` in Visual Studio 2026, or run:

```powershell
dotnet build .\ClashSubscriptionUploader.csproj
```

## Behavior

- The “获取OpenRung订阅” button requests the official `GET /api/v1/relays` candidate list and validates its Ed25519 signature, channel, requested limit, and freshness before conversion. Candidate count defaults to 10 and can be changed up to 50.
- Only direct `vless-reality-vision` entries with a live descriptor are converted. The generic Clash/Mihomo output does not implement OpenRung's NAT-punch or WSS/CDN fallback, so those transport paths are not exported as if they were ordinary VLESS nodes.
- The result is previewed and saved to `Documents\ClashSubscriptionUploader\clash.yaml`. “上传订阅” uploads the currently previewed subscription separately.
- Generated rules send private/local IPv4 and IPv6 ranges, China GeoSite domains, and China GeoIP addresses directly; all remaining traffic uses the OpenRung proxy group.
- OpenRung's exact bundled country `.srs` files are not returned by its Broker API and are not used here; this config uses Mihomo's `GEOSITE,CN` and `GEOIP,CN` rules instead.
- FTP host, port, account, password, upload path, and FTPS setting are configured in the window. The password is saved encrypted with Windows DPAPI for the current Windows user after the app closes; enter it once on first run.
- Host, port, account, upload path, and FTPS preference are saved in the current Windows user's Local AppData. On exit, the password is encrypted with Windows DPAPI for the current user and saved there.

- “获取Zhuhai订阅” downloads the configured public Clash YAML and loads it into the same preview/upload flow.
- The FTP panel connects to the FTP root and supports directory navigation, refresh, file upload/download, drag-and-drop upload, folder creation, rename, and delete. FTP folders must be empty before they can be deleted.
- The default remote path is `wwwroot/clash.asp`, relative to the FTP root. Change that full path in the window if another server uses a different website directory.
- Upload uses FTP `STOR`, which replaces an existing file with the same path and name.
- The repository contains no FTP password or generated subscription credentials.

## Security

The generated YAML contains live relay connection credentials and is time-sensitive. Protect the website path so only you can download it. Plain FTP does not encrypt login credentials or file contents; enable FTPS only if your provider supports explicit FTPS on the configured port. Do not publish this local project or the generated subscription.
