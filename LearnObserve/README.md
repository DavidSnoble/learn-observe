# Learn Observe

Small ops dashboard for Learn.

## What it does
- Shows `systemctl --user` status for `learn.service` and `cloudflared.service`
- Shows recent `journalctl --user` warnings/errors

## Run locally

```bash
cd LearnObserve
DOTNET_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:3002 dotnet run
```

Then open: http://127.0.0.1:3002

## Config
Uses `.env`-style environment variables:
- `GOOGLE_CLIENT_ID`
- `GOOGLE_CLIENT_SECRET`
- `ADMIN_EMAILS` (comma-separated)

## Deploy
Create a systemd **user** service (recommended) similar to Learn.
