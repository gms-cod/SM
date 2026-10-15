#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Publish Startup Monitor installer to GitHub Releases (both mirrors).

Создаёт или обновляет релиз с тегом ``v<version>`` в репозиториях
``gms-cod/SM`` и ``gms-cod/Startup-Monitor`` через GitHub REST API
и загружает ассеты инсталлятора (.exe + .sha256).

Использование (PowerShell / cmd):
    set GITHUB_TOKEN=ghp_...
    python scripts/publish_github_release.py
    python scripts/publish_github_release.py --version 1.3.0
    python scripts/publish_github_release.py --version 1.3.0 ^
        --installer installers/StartupMonitorSetup-1.3.0.exe ^
        --sha256 installers/StartupMonitorSetup-1.3.0.exe.sha256

Требуется только стандартная библиотека Python (urllib).
Токен берётся из ``--token``, ``GITHUB_TOKEN`` или ``GH_TOKEN``.
Нужны права (scope): ``repo`` / ``contents:write``.
"""

from __future__ import annotations

import argparse
import json
import mimetypes
import os
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

DEFAULT_VERSION = "1.4.0"
DEFAULT_REPOS = ("gms-cod/SM", "gms-cod/Startup-Monitor")
DEFAULT_RELEASE_NAME_TEMPLATE = "Startup Monitor v{version} \u2014 Windows Firewall & Port Defender"
API_URL = "https://api.github.com"
USER_AGENT = "StartupMonitor-release-publisher/1.0"
VERIFY_REPO = "gms-cod/SM"

REPO_ROOT = Path(__file__).resolve().parent.parent


# ---------------------------------------------------------------------------
# HTTP helpers (stdlib only)
# ---------------------------------------------------------------------------

class GitHubApiError(RuntimeError):
    def __init__(self, message: str, status: int | None = None, body: str = ""):
        super().__init__(message)
        self.status = status
        self.body = body


def _read_response(resp: urllib.request.addinfourl) -> tuple[int, object, object]:
    status = getattr(resp, "status", 200) or 200
    raw = resp.read()
    headers = resp.headers
    if not raw:
        return status, None, headers
    try:
        return status, json.loads(raw.decode("utf-8")), headers
    except (UnicodeDecodeError, json.JSONDecodeError):
        return status, raw.decode("utf-8", errors="replace"), headers


def api_request(
    method: str,
    url: str,
    token: str,
    payload: object | None = None,
    extra_headers: dict | None = None,
    data_bytes: bytes | None = None,
    timeout: int = 60,
    retries: int = 3,
) -> tuple[int, object]:
    """Perform a GitHub API request, return (status, parsed_json_or_text).

    Raises GitHubApiError on HTTP error (except caller may pre-check 404).
    Retries transient 5xx / network errors.
    """
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "User-Agent": USER_AGENT,
        "X-GitHub-Api-Version": "2022-11-28",
    }
    body: bytes | None = None
    if payload is not None:
        body = json.dumps(payload).encode("utf-8")
        headers["Content-Type"] = "application/json"
    elif data_bytes is not None:
        body = data_bytes
    if extra_headers:
        headers.update(extra_headers)

    last_err: Exception | None = None
    for attempt in range(retries):
        req = urllib.request.Request(url, data=body, headers=headers, method=method)
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                status, parsed, _ = _read_response(resp)
                return status, parsed
        except urllib.error.HTTPError as exc:
            try:
                err_raw = exc.read().decode("utf-8", errors="replace")
            except Exception:
                err_raw = ""
            # Retry only on transient server errors.
            if exc.code in (500, 502, 503, 504) and attempt < retries - 1:
                last_err = exc
                time.sleep(2 ** attempt)
                continue
            raise GitHubApiError(
                f"{method} {url} -> HTTP {exc.code}: {err_raw[:2000]}",
                status=exc.code,
                body=err_raw,
            ) from exc
        except (urllib.error.URLError, TimeoutError, OSError) as exc:
            last_err = exc
            if attempt < retries - 1:
                time.sleep(2 ** attempt)
                continue
            raise GitHubApiError(f"{method} {url} -> network error: {exc}") from exc
    raise GitHubApiError(f"{method} {url} -> failed after {retries} attempts: {last_err}")


def api_get_maybe(url: str, token: str, timeout: int = 60) -> tuple[int, object]:
    """GET that returns (status, body) without raising on 404/4xx."""
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "User-Agent": USER_AGENT,
        "X-GitHub-Api-Version": "2022-11-28",
    }
    req = urllib.request.Request(url, headers=headers, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            status, parsed, _ = _read_response(resp)
            return status, parsed
    except urllib.error.HTTPError as exc:
        try:
            err_raw = exc.read().decode("utf-8", errors="replace")
        except Exception:
            err_raw = ""
        parsed: object = err_raw
        try:
            parsed = json.loads(err_raw) if err_raw else None
        except json.JSONDecodeError:
            pass
        return exc.code, parsed


# ---------------------------------------------------------------------------
# Release operations
# ---------------------------------------------------------------------------

def get_release_by_tag(api_base: str, repo: str, tag: str, token: str) -> dict | None:
    url = f"{api_base}/repos/{repo}/releases/tags/{urllib.parse.quote(tag, safe='')}"
    status, data = api_get_maybe(url, token)
    if status == 200 and isinstance(data, dict):
        return data
    if status == 404:
        return None
    raise GitHubApiError(f"GET {url} -> HTTP {status}: {str(data)[:2000]}", status=status)


def create_release(
    api_base: str,
    repo: str,
    token: str,
    tag: str,
    name: str,
    body: str,
    draft: bool,
    prerelease: bool,
    target_commitish: str | None,
) -> dict:
    url = f"{api_base}/repos/{repo}/releases"
    payload: dict = {
        "tag_name": tag,
        "name": name,
        "body": body,
        "draft": draft,
        "prerelease": prerelease,
        "generate_release_notes": False,
    }
    if target_commitish:
        payload["target_commitish"] = target_commitish
    status, data = api_request("POST", url, token, payload=payload)
    if status not in (200, 201) or not isinstance(data, dict):
        raise GitHubApiError(f"POST {url} -> HTTP {status}: {str(data)[:2000]}", status=status)
    return data


def update_release(
    api_base: str,
    repo: str,
    token: str,
    release_id: int,
    name: str,
    body: str,
    draft: bool,
    prerelease: bool,
    tag: str,
) -> dict:
    url = f"{api_base}/repos/{repo}/releases/{release_id}"
    payload = {
        "tag_name": tag,
        "name": name,
        "body": body,
        "draft": draft,
        "prerelease": prerelease,
    }
    status, data = api_request("PATCH", url, token, payload=payload)
    if status != 200 or not isinstance(data, dict):
        raise GitHubApiError(f"PATCH {url} -> HTTP {status}: {str(data)[:2000]}", status=status)
    return data


def list_assets(api_base: str, repo: str, token: str, release_id: int) -> list:
    url = f"{api_base}/repos/{repo}/releases/{release_id}/assets?per_page=100"
    status, data = api_request("GET", url, token)
    if status != 200 or not isinstance(data, list):
        raise GitHubApiError(f"GET {url} -> HTTP {status}", status=status)
    return data


def delete_asset_by_url(token: str, asset_url: str) -> None:
    status, _ = api_request("DELETE", asset_url, token)
    if status not in (200, 204):
        raise GitHubApiError(f"DELETE {asset_url} -> HTTP {status}", status=status)


def upload_asset(token: str, upload_url_template: str, file_path: Path, timeout: int = 600) -> dict:
    base = upload_url_template.split("{")[0]
    url = f"{base}?name={urllib.parse.quote(file_path.name)}"
    content_type, _ = mimetypes.guess_type(file_path.name)
    if content_type is None:
        content_type = "application/octet-stream"
    data = file_path.read_bytes()
    extra = {
        "Content-Type": content_type,
        "Content-Length": str(len(data)),
    }
    status, parsed = api_request("POST", url, token, data_bytes=data, extra_headers=extra, timeout=timeout, retries=3)
    if status not in (200, 201) or not isinstance(parsed, dict):
        raise GitHubApiError(f"POST {url} -> HTTP {status}: {str(parsed)[:2000]}", status=status)
    return parsed


def default_body(version: str, installer_name: str, sha256_text: str) -> str:
    return (
        f"## Startup Monitor v{version}\n"
        f"\n"
        f"Windows Firewall & Port Defender \u2014 \u043c\u043e\u043d\u0438\u0442\u043e\u0440\u0438\u043d\u0433 "
        f"\u0430\u0432\u0442\u043e\u0437\u0430\u0433\u0440\u0443\u0437\u043a\u0438, firewall-\u043f\u0440\u0430\u0432\u0438\u043b "
        f"\u0438 \u0437\u0430\u0449\u0438\u0442\u0430 \u043f\u043e\u0440\u0442\u043e\u0432.\n"
        f"\n"
        f"### \U0001f4e6 \u0423\u0441\u0442\u0430\u043d\u043e\u0432\u043a\u0430\n"
        f"- \u0417\u0430\u043f\u0443\u0441\u0442\u0438\u0442\u0435 **`{installer_name}`** "
        f"\u043e\u0442 \u0438\u043c\u0435\u043d\u0438 \u0430\u0434\u043c\u0438\u043d\u0438\u0441\u0442\u0440\u0430\u0442\u043e\u0440\u0430.\n"
        f"- \u0422\u0438\u0445\u0438\u0439 \u0440\u0435\u0436\u0438\u043c: `/SILENT /NORESTART /SUPPRESSMSGBOXES`.\n"
        f"\n"
        f"### \U0001f512 \u041a\u043e\u043d\u0442\u0440\u043e\u043b\u044c\u043d\u0430\u044f \u0441\u0443\u043c\u043c\u0430 (SHA-256)\n"
        f"```text\n"
        f"{sha256_text.strip()}\n"
        f"```\n"
    )


# ---------------------------------------------------------------------------
# Verify
# ---------------------------------------------------------------------------

def verify_latest(
    api_base: str,
    token: str | None,
    repo: str,
    expected_tag: str,
    expected_asset_substring: str,
) -> dict:
    """Check GET /repos/{repo}/releases/latest.

    Requires HTTP 200, tag_name == expected_tag and at least one asset whose
    browser_download_url contains expected_asset_substring.
    """
    url = f"{api_base}/repos/{repo}/releases/latest"
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": USER_AGENT,
        "X-GitHub-Api-Version": "2022-11-28",
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    req = urllib.request.Request(url, headers=headers, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            status, data, _ = _read_response(resp)
    except urllib.error.HTTPError as exc:
        try:
            err_raw = exc.read().decode("utf-8", errors="replace")
        except Exception:
            err_raw = ""
        raise GitHubApiError(
            f"VERIFY: GET {url} -> HTTP {exc.code}: {err_raw[:2000]}", status=exc.code
        ) from exc
    if status != 200 or not isinstance(data, dict):
        raise GitHubApiError(f"VERIFY: GET {url} -> HTTP {status} (expected 200)", status=status)
    tag = data.get("tag_name")
    if tag != expected_tag:
        raise GitHubApiError(
            f"VERIFY: tag_name={tag!r}, expected {expected_tag!r} (release: {data.get('html_url')})"
        )
    assets = data.get("assets") or []
    urls = [a.get("browser_download_url", "") for a in assets if isinstance(a, dict)]
    if not any(expected_asset_substring in (u or "") for u in urls):
        raise GitHubApiError(
            f"VERIFY: no asset browser_download_url contains "
            f"{expected_asset_substring!r}. Got: {urls}"
        )
    return data


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def resolve_default(path_arg: str | None, version: str, kind: str) -> Path:
    if path_arg:
        p = Path(path_arg)
        return p if p.is_absolute() else (REPO_ROOT / p)
    name = f"StartupMonitorSetup-{version}.exe" + (".sha256" if kind == "sha256" else "")
    return REPO_ROOT / "installers" / name


def parse_args(argv: list | None = None) -> argparse.Namespace:
    ap = argparse.ArgumentParser(
        description="Create/update GitHub releases and upload installer assets (gms-cod/SM + gms-cod/Startup-Monitor).",
    )
    ap.add_argument("--version", default=DEFAULT_VERSION, help="Product version, e.g. 1.3.0 (default: %(default)s)")
    ap.add_argument("--installer", default=None, help="Path to setup .exe (default: installers/StartupMonitorSetup-<version>.exe)")
    ap.add_argument("--sha256", default=None, help="Path to .sha256 file (default: <installer>.sha256)")
    ap.add_argument("--repos", nargs="+", default=list(DEFAULT_REPOS), help="Target repos (default: %(default)s)")
    ap.add_argument("--tag", default=None, help="Release tag (default: v<version>)")
    ap.add_argument("--release-name", default=None, help="Release name (default: 'Startup Monitor v<version> — Windows Firewall & Port Defender')")
    ap.add_argument("--body", default=None, help="Release notes body text (default: auto-generated)")
    ap.add_argument("--body-file", default=None, help="Read release notes body from file")
    ap.add_argument("--target-commitish", default=None, help="Target branch/commit for new tag (default: repo default branch)")
    ap.add_argument("--draft", action="store_true", default=False, help="Create as draft")
    ap.add_argument("--prerelease", action="store_true", default=False, help="Mark as prerelease")
    ap.add_argument("--token", default=None, help="GitHub token (default: $GITHUB_TOKEN / $GH_TOKEN)")
    ap.add_argument("--api-url", default=API_URL, help="GitHub API base URL (default: %(default)s)")
    ap.add_argument("--skip-verify", action="store_true", default=False, help="Skip final 'releases/latest' verification")
    ap.add_argument("--verify-only", action="store_true", default=False, help="Only run verification, do not publish")
    return ap.parse_args(argv)


def main(argv: list | None = None) -> int:
    args = parse_args(argv)
    version = args.version.strip()
    tag = args.tag or f"v{version}"
    release_name = args.release_name or DEFAULT_RELEASE_NAME_TEMPLATE.format(version=version)

    token = args.token or os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
    api_base = (args.api_url or API_URL).rstrip("/")

    installer = resolve_default(args.installer, version, "exe")
    sha256_file = resolve_default(args.sha256, version, "sha256")
    # --sha256 default must track a custom --installer path when only installer given.
    if args.installer and not args.sha256:
        sha256_file = Path(str(installer) + ".sha256")

    print(f"Version      : {version}")
    print(f"Tag          : {tag}")
    print(f"Release name : {release_name}")
    print(f"Repos        : {', '.join(args.repos)}")
    print(f"Installer    : {installer}")
    print(f"SHA-256 file : {sha256_file}")

    if args.verify_only:
        if not args.skip_verify:
            try:
                data = verify_latest(api_base, token or None, VERIFY_REPO, tag, installer.name)
                print(f"VERIFY OK: tag_name={data.get('tag_name')} assets={[a.get('name') for a in data.get('assets', [])]}")
                print(f"Release URL: {data.get('html_url')}")
                return 0
            except GitHubApiError as exc:
                print(f"VERIFY FAILED: {exc}", file=sys.stderr)
                return 1
        print("--verify-only combined with --skip-verify: nothing to do.")
        return 0

    if not installer.is_file():
        print(f"ERROR: installer not found: {installer}", file=sys.stderr)
        return 2
    if not sha256_file.is_file():
        print(f"ERROR: sha256 file not found: {sha256_file}", file=sys.stderr)
        return 2
    if not token:
        print("ERROR: GitHub token not provided. Use --token or set GITHUB_TOKEN env var.", file=sys.stderr)
        return 2

    try:
        sha256_text = sha256_file.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        print(f"ERROR: cannot read sha256 file: {exc}", file=sys.stderr)
        return 2

    if args.body_file:
        try:
            body = Path(args.body_file).read_text(encoding="utf-8")
        except OSError as exc:
            print(f"ERROR: cannot read --body-file: {exc}", file=sys.stderr)
            return 2
    elif args.body is not None:
        body = args.body
    else:
        body = default_body(version, installer.name, sha256_text)

    size_mb = installer.stat().st_size / (1024 * 1024)
    print(f"Installer size: {size_mb:.2f} MB")

    failed: list[str] = []
    for repo in args.repos:
        print(f"\n=== {repo} ===")
        try:
            existing = get_release_by_tag(api_base, repo, tag, token)
            if existing is None:
                print(f"Release {tag} not found -> creating (draft={args.draft}, prerelease={args.prerelease})...")
                release = create_release(
                    api_base, repo, token, tag, release_name, body,
                    args.draft, args.prerelease, args.target_commitish,
                )
                print(f"Created release id={release.get('id')} html_url={release.get('html_url')}")
            else:
                print(f"Release {tag} exists (id={existing.get('id')}) -> updating metadata...")
                release = update_release(
                    api_base, repo, token, int(existing["id"]),
                    release_name, body, args.draft, args.prerelease, tag,
                )
                print(f"Updated release html_url={release.get('html_url')}")

            release_id = int(release["id"])
            upload_url = str(release.get("upload_url") or "")
            if not upload_url:
                raise GitHubApiError("Release response has no upload_url")

            assets = list_assets(api_base, repo, token, release_id)
            by_name = {a.get("name"): a for a in assets if isinstance(a, dict)}

            for path in (installer, sha256_file):
                old = by_name.get(path.name)
                if old and old.get("url"):
                    print(f"Asset '{path.name}' exists (id={old.get('id')}) -> deleting...")
                    delete_asset_by_url(token, str(old["url"]))
                elif old:
                    print(f"Asset '{path.name}' exists but has no URL; uploading anyway (GitHub will reject duplicates)...")
                print(f"Uploading '{path.name}' ({path.stat().st_size} bytes)...")
                uploaded = upload_asset(token, upload_url, path)
                print(f"  OK: {uploaded.get('name')} -> {uploaded.get('browser_download_url')}")
            print(f"DONE: {repo} -> {release.get('html_url')}")
        except GitHubApiError as exc:
            print(f"FAILED [{repo}]: {exc}", file=sys.stderr)
            failed.append(repo)
        except OSError as exc:
            print(f"FAILED [{repo}]: file error: {exc}", file=sys.stderr)
            failed.append(repo)

    if failed:
        print(f"\nPublish failed for: {', '.join(failed)}", file=sys.stderr)
        return 1

    if args.skip_verify:
        print("\nVerification skipped (--skip-verify).")
        return 0

    print(f"\n=== Verifying GET {api_base}/repos/{VERIFY_REPO}/releases/latest ===")
    try:
        data = verify_latest(api_base, token, VERIFY_REPO, tag, installer.name)
        asset_names = [a.get("name") for a in data.get("assets", []) if isinstance(a, dict)]
        print(f"VERIFY OK: status=200 tag_name={data.get('tag_name')!r} assets={asset_names}")
        for a in data.get("assets", []):
            if isinstance(a, dict) and installer.name in str(a.get("browser_download_url") or ""):
                print(f"Installer URL: {a.get('browser_download_url')}")
        print(f"Release URL: {data.get('html_url')}")
        return 0
    except GitHubApiError as exc:
        print(f"VERIFY FAILED: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
