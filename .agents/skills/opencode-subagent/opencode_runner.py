"""
OpenCode Subagent Runner for Antigravity (Gemini Orchestrator).
Delegates coding, testing, refactoring, and documentation tasks
to free OpenCode subagents, specifically:
opencode/muse-spark-1.3-contributor-free
"""

import os
import sys
import shutil
import subprocess
import time
from dataclasses import dataclass, field
from typing import List, Optional, Dict, Any


DEFAULT_MODEL = "opencode/deepseek-v4.1-flash-free"


@dataclass
class SubagentResult:
    success: bool
    exit_code: int
    stdout: str
    stderr: str
    duration_seconds: float
    model: str
    command: List[str] = field(default_factory=list)

    def to_dict(self) -> Dict[str, Any]:
        return {
            "success": self.success,
            "exit_code": self.exit_code,
            "stdout": self.stdout,
            "stderr": self.stderr,
            "duration_seconds": round(self.duration_seconds, 2),
            "model": self.model,
            "command": " ".join(self.command),
        }


def find_opencode_binary() -> str:
    """Finds the direct opencode executable (opencode.exe or opencode) on Windows/Linux."""
    # Check direct npm binary on Windows first to avoid cmd.exe shell wrapper issues
    appdata = os.environ.get("APPDATA", "")
    if appdata:
        npm_exe = os.path.join(appdata, "npm", "node_modules", "opencode-ai", "bin", "opencode.exe")
        if os.path.exists(npm_exe):
            return npm_exe

    # Check PATH
    cmd = shutil.which("opencode.exe") or shutil.which("opencode")
    if cmd and os.path.exists(cmd):
        return cmd

    if appdata:
        npm_cmd = os.path.join(appdata, "npm", "opencode.cmd")
        if os.path.exists(npm_cmd):
            return npm_cmd

    return "opencode"


def run_subagent_task(
    prompt: str,
    model: str = DEFAULT_MODEL,
    work_dir: Optional[str] = None,
    files: Optional[List[str]] = None,
    variant: Optional[str] = None,
    auto_approve: bool = True,
    thinking: bool = False,
    timeout_seconds: int = 600,
) -> SubagentResult:
    """
    Executes a task via OpenCode subagent in headless non-interactive mode.

    :param prompt: The specific prompt/instruction for the subagent.
    :param model: The model ID (defaults to opencode/muse-spark-1.3-contributor-free).
    :param work_dir: Directory where the task is executed.
    :param files: Optional list of files to attach to the task.
    :param variant: Reasoning effort (e.g. minimal, low, medium, high, xhigh).
    :param auto_approve: Auto-approve safe tool/file operations.
    :param thinking: Include thinking blocks in output.
    :param timeout_seconds: Maximum time to wait before terminating.
    :return: SubagentResult with execution status and output.
    """
    binary = find_opencode_binary()
    cwd = work_dir or os.getcwd()

    full_prompt = prompt
    if files:
        file_list_str = "\n".join([f"- {f}" for f in files])
        full_prompt = f"{prompt}\n\nЦелевые файлы для задачи:\n{file_list_str}"

    cmd = [binary, "run", "--pure", "--model", model]

    if auto_approve:
        cmd.append("--auto")

    if variant:
        cmd.extend(["--variant", variant])

    if thinking:
        cmd.append("--thinking")

    # Append prompt at the end
    cmd.append(full_prompt)

    start_time = time.time()
    try:
        proc = subprocess.Popen(
            cmd,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            bufsize=1,
            encoding="utf-8",
            errors="replace",
            stdin=subprocess.DEVNULL,
            shell=False,
        )

        stdout_lines = []
        last_line = ""
        
        # Читаем строки в реальном времени по мере их генерации субагентом
        while True:
            line = proc.stdout.readline()
            if not line and proc.poll() is not None:
                break
            if line:
                stdout_lines.append(line)
                stripped = line.strip()
                if stripped:
                    last_line = stripped
                    
            if time.time() - start_time > timeout_seconds:
                proc.kill()
                raise subprocess.TimeoutExpired(cmd, timeout_seconds)

        proc.wait()
        duration = time.time() - start_time
        full_stdout = "".join(stdout_lines)
        
        is_rate_limited = "Rate limit exceeded" in full_stdout
        success = proc.returncode == 0 and not is_rate_limited
        err_msg = ""
        if is_rate_limited:
            err_msg = "Provider rate limit exceeded. Please wait a moment."

        return SubagentResult(
            success=success,
            exit_code=proc.returncode if not is_rate_limited else 429,
            stdout=full_stdout,
            stderr=err_msg,
            duration_seconds=duration,
            model=model,
            command=cmd,
        )
    except subprocess.TimeoutExpired as e:
        duration = time.time() - start_time
        return SubagentResult(
            success=False,
            exit_code=-1,
            stdout=(e.stdout or "") if isinstance(e.stdout, str) else "",
            stderr=f"Task timed out after {timeout_seconds} seconds.",
            duration_seconds=duration,
            model=model,
            command=cmd,
        )
    except Exception as e:
        duration = time.time() - start_time
        return SubagentResult(
            success=False,
            exit_code=-2,
            stdout="",
            stderr=f"Error executing subagent process: {str(e)}",
            duration_seconds=duration,
            model=model,
            command=cmd,
        )


def main():
    import argparse
    import json

    parser = argparse.ArgumentParser(
        description="Run task via free OpenCode subagent (muse-spark-1.3-contributor-free)"
    )
    parser.add_argument("prompt", help="Instruction for the subagent")
    parser.add_argument("--model", default=DEFAULT_MODEL, help="Model ID to use")
    parser.add_argument("-d", "--dir", default=None, help="Working directory")
    parser.add_argument("-f", "--files", nargs="*", default=None, help="Files to attach")
    parser.add_argument("--variant", default="medium", help="Reasoning variant (minimal, low, medium, high, xhigh)")
    parser.add_argument("--no-auto", dest="auto", action="store_false", help="Disable auto-approve")
    parser.add_argument("--thinking", action="store_true", help="Show thinking blocks")
    parser.add_argument("--json", action="store_true", help="Output result as JSON")
    parser.add_argument("--timeout", type=int, default=600, help="Timeout in seconds")

    args = parser.parse_args()

    result = run_subagent_task(
        prompt=args.prompt,
        model=args.model,
        work_dir=args.dir,
        files=args.files,
        variant=args.variant,
        auto_approve=args.auto,
        thinking=args.thinking,
        timeout_seconds=args.timeout,
    )

    if args.json:
        print(json.dumps(result.to_dict(), ensure_ascii=False, indent=2))
    else:
        if result.success:
            print(result.stdout)
        else:
            print(f"FAILED (Exit Code {result.exit_code}):", file=sys.stderr)
            if result.stdout:
                print(result.stdout)
            if result.stderr:
                print(result.stderr, file=sys.stderr)
        print(f"\n[Completed in {result.duration_seconds:.2f}s using {result.model}]")

    sys.exit(result.exit_code if result.exit_code >= 0 else 1)


if __name__ == "__main__":
    main()
