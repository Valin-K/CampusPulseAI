from __future__ import annotations

import time
from datetime import datetime, timezone
from typing import Any, Callable

import requests


LAB_REQUIRED_TOOLS = (
    "student_count",
    "students_by_subject",
    "project_files",
    "ci_report",
)
SAFE_VALIDATION_ARGUMENTS = {
    "student_count": {},
    "students_by_subject": {"subject_code": "CS101"},
    "project_files": {"directory_path": "."},
    "ci_report": {},
}


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat()


class McpCollector:
    """Collect live MCP contracts and execute each Lab 07 validation tool."""

    def __init__(
        self,
        server_url: str,
        timeout: int = 10,
        request_func: Callable[..., Any] | None = None,
    ) -> None:
        self.server_url = server_url.rstrip("/")
        self.timeout = timeout
        self.request_func = request_func or requests.request

    def _request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
    ) -> dict[str, Any]:
        url = f"{self.server_url}{path}"
        started_at = _now_iso()
        started = time.perf_counter()
        request_record = {"method": method, "url": url, "body": payload}
        try:
            response = self.request_func(
                method,
                url,
                json=payload,
                timeout=self.timeout,
            )
            try:
                body = response.json()
            except ValueError:
                body = response.text
            status_code = response.status_code
            return {
                "requestedAtUtc": started_at,
                "completedAtUtc": _now_iso(),
                "durationSeconds": round(time.perf_counter() - started, 3),
                "request": request_record,
                "response": {"statusCode": status_code, "body": body},
                "passed": status_code == 200,
                "error": None if status_code == 200 else f"HTTP {status_code}",
            }
        except Exception as exc:
            return {
                "requestedAtUtc": started_at,
                "completedAtUtc": _now_iso(),
                "durationSeconds": round(time.perf_counter() - started, 3),
                "request": request_record,
                "response": None,
                "passed": False,
                "error": f"{type(exc).__name__}: {exc}",
            }

    @staticmethod
    def _check(
        name: str,
        passed: bool,
        output: str,
        duration: float = 0,
        skipped: bool = False,
    ) -> dict[str, Any]:
        return {
            "name": name,
            "passed": passed,
            "skipped": skipped,
            "exitCode": 0 if passed or skipped else 1,
            "durationSeconds": duration,
            "output": output,
        }

    def inspect(self) -> dict[str, Any]:
        health = self._request("GET", "/health")
        registry = self._request("GET", "/api/mcp/tools")
        health_body = (health.get("response") or {}).get("body")
        registry_body = (registry.get("response") or {}).get("body")

        tools = registry_body if isinstance(registry_body, list) else []
        valid_tool_records = all(
            isinstance(tool, dict)
            and isinstance(tool.get("name"), str)
            and bool(tool.get("name"))
            and isinstance(tool.get("description"), str)
            and isinstance(tool.get("owner"), str)
            and isinstance(tool.get("inputSchema"), dict)
            for tool in tools
        )
        tool_names = {
            tool["name"].casefold()
            for tool in tools
            if isinstance(tool, dict) and isinstance(tool.get("name"), str)
        }
        missing_tools = [
            name for name in LAB_REQUIRED_TOOLS if name.casefold() not in tool_names
        ]

        health_valid = (
            health.get("passed") is True
            and isinstance(health_body, dict)
            and health_body.get("status") == "healthy"
            and health_body.get("service") == "mcp"
            and isinstance(health_body.get("toolCount"), int)
            and health_body["toolCount"] == len(tools)
        )
        registry_valid = (
            registry.get("passed") is True
            and isinstance(registry_body, list)
            and valid_tool_records
        )
        required_tools_valid = registry_valid and not missing_tools

        checks = [
            self._check(
                "MCP health",
                health_valid,
                health.get("error")
                or (
                    f"service={health_body.get('service')}; "
                    f"registered tools={health_body.get('toolCount')}"
                    if isinstance(health_body, dict)
                    else "MCP health response did not match its contract."
                ),
                health.get("durationSeconds", 0),
            ),
            self._check(
                "MCP tool registry",
                registry_valid,
                registry.get("error")
                or (
                    f"Validated {len(tools)} live tool contracts."
                    if registry_valid
                    else "Tool registry must be a list of complete tool contracts."
                ),
                registry.get("durationSeconds", 0),
            ),
            self._check(
                "MCP Lab 07 tool set",
                required_tools_valid,
                "All four Lab 07 tools are registered."
                if required_tools_valid
                else "Missing Lab 07 tools: " + ", ".join(missing_tools),
            ),
        ]

        return {
            "passed": all(check["passed"] for check in checks),
            "healthEvidence": health,
            "registryEvidence": registry,
            "tools": tools if registry_valid else [],
            "missingRequiredTools": missing_tools,
            "checks": checks,
        }

    def prepare_tool_call(
        self,
        tools: list[dict[str, Any]],
        tool_name: str,
        arguments: dict[str, Any],
    ) -> dict[str, Any]:
        errors: list[str] = []
        if not isinstance(arguments, dict):
            arguments = {}
            errors.append("Tool arguments must be a JSON object.")

        selected = next(
            (
                tool for tool in tools
                if isinstance(tool, dict)
                and isinstance(tool.get("name"), str)
                and tool["name"].casefold() == tool_name.casefold()
            ),
            None,
        )
        if selected is None or tool_name.casefold() not in {
            name.casefold() for name in LAB_REQUIRED_TOOLS
        }:
            errors.append("The selected tool is not in the live Lab 07 tool set.")
            return {
                "valid": False,
                "tool": tool_name,
                "arguments": arguments,
                "errors": errors,
            }

        schema = selected.get("inputSchema")
        if not isinstance(schema, dict):
            errors.append("The selected tool has no valid input schema.")
            schema = {}

        effective_arguments = dict(arguments)
        if tool_name.casefold() == "project_files" and not effective_arguments:
            effective_arguments = {"directory_path": "."}

        unknown_arguments = sorted(set(effective_arguments) - set(schema))
        if unknown_arguments:
            errors.append(
                "Arguments are not declared by the live tool schema: "
                + ", ".join(unknown_arguments)
            )

        required_arguments = [
            name for name in schema
            if not (
                tool_name.casefold() == "project_files"
                and name == "directory_path"
            )
        ]
        for name in required_arguments:
            if name not in effective_arguments:
                errors.append(f"A value is required for '{name}'.")
        for name, value in effective_arguments.items():
            if not isinstance(value, str) or not value.strip():
                errors.append(f"A non-empty string is required for '{name}'.")

        return {
            "valid": not errors,
            "tool": selected["name"],
            "toolContract": selected,
            "arguments": effective_arguments,
            "errors": errors,
        }

    def execute_tool(self, prepared_call: dict[str, Any]) -> dict[str, Any]:
        if not prepared_call.get("valid"):
            return {
                "tool": prepared_call.get("tool"),
                "arguments": prepared_call.get("arguments", {}),
                "executed": False,
                "passed": False,
                "requestedAtUtc": None,
                "completedAtUtc": None,
                "durationSeconds": 0,
                "request": None,
                "response": None,
                "resultContractValid": False,
                "error": "; ".join(prepared_call.get("errors", [])),
            }

        tool_name = prepared_call["tool"]
        arguments = prepared_call["arguments"]
        call = self._request(
            "POST",
            "/api/mcp/tools/call",
            {"tool": tool_name, "arguments": arguments},
        )
        response = call.get("response") or {}
        body = response.get("body")
        valid_envelope = (
            call.get("passed") is True
            and isinstance(body, dict)
            and isinstance(body.get("tool"), str)
            and body["tool"].casefold() == tool_name.casefold()
            and body.get("isError") is False
            and "result" in body
        )
        error = call.get("error")
        result_error = None
        if valid_envelope:
            result_error = self._validate_tool_result(
                tool_name,
                arguments,
                body["result"],
            )
        if not valid_envelope and error is None:
            error = (
                "MCP tool returned an error."
                if isinstance(body, dict) and body.get("isError") is True
                else "MCP tool response did not match the invocation contract."
            )
        elif result_error:
            error = result_error

        return {
            "tool": tool_name,
            "arguments": arguments,
            "executed": True,
            "passed": valid_envelope and result_error is None,
            "requestedAtUtc": call["requestedAtUtc"],
            "completedAtUtc": call["completedAtUtc"],
            "durationSeconds": call["durationSeconds"],
            "request": call["request"],
            "response": call["response"],
            "resultContractValid": valid_envelope and result_error is None,
            "error": error,
        }

    @staticmethod
    def _validate_tool_result(
        tool_name: str,
        arguments: dict[str, Any],
        result: Any,
    ) -> str | None:
        if not isinstance(result, dict):
            return "Tool result must be a JSON object."
        if result.get("error"):
            return f"Tool reported an error: {result['error']}"

        if tool_name == "student_count":
            count = result.get("student_count")
            if type(count) is not int or count < 0:
                return "student_count must return a non-negative integer student_count."
        elif tool_name == "students_by_subject":
            count = result.get("student_count")
            if result.get("subject_code") != arguments.get("subject_code"):
                return "students_by_subject returned a different subject_code."
            if type(count) is not int or count < 0:
                return "students_by_subject must return a non-negative integer student_count."
        elif tool_name == "project_files":
            files = result.get("files")
            if not isinstance(result.get("path"), str):
                return "project_files must return the inspected path."
            if not isinstance(files, list) or not all(
                isinstance(file_name, str) for file_name in files
            ):
                return "project_files must return a list of file and directory names."
        elif tool_name == "ci_report":
            if not isinstance(result.get("path"), str):
                return "ci_report must return the path of the latest evidence report."
            if not isinstance(result.get("report"), str):
                return "ci_report must return the report contents as text."
        return None

    def collect_all(self) -> dict[str, Any]:
        """Execute the four Lab 07 tools and capture each live request/result."""
        inspection = self.inspect()
        checks = list(inspection["checks"])
        tool_executions: dict[str, dict[str, Any]] = {}

        if inspection["passed"]:
            for tool_name in LAB_REQUIRED_TOOLS:
                prepared_call = self.prepare_tool_call(
                    inspection["tools"],
                    tool_name,
                    dict(SAFE_VALIDATION_ARGUMENTS[tool_name]),
                )
                invocation = self.execute_tool(prepared_call)
                tool_executions[tool_name] = invocation
                checks.append(self._check(
                    f"MCP {tool_name} tool execution",
                    invocation["passed"],
                    invocation["error"] or "Tool execution and result contract validated.",
                    invocation["durationSeconds"],
                    skipped=not invocation["executed"],
                ))
        else:
            for tool_name in LAB_REQUIRED_TOOLS:
                checks.append(self._check(
                    f"MCP {tool_name} tool execution",
                    False,
                    "Skipped because MCP health, registry, or required tool validation failed.",
                    skipped=True,
                ))

        return {
            **inspection,
            "passed": inspection["passed"] and all(
                execution.get("passed") is True
                for execution in tool_executions.values()
            ) and len(tool_executions) == len(LAB_REQUIRED_TOOLS),
            "toolExecutions": tool_executions,
            "checks": checks,
        }

    def run_smoke_checks(self) -> list[dict[str, Any]]:
        """Keep full mode's existing repository-tool MCP smoke check narrow."""
        inspection = self.inspect()
        checks = list(inspection["checks"])
        if inspection["passed"]:
            prepared_call = self.prepare_tool_call(
                inspection["tools"],
                "project_files",
                {"directory_path": "."},
            )
            invocation = self.execute_tool(prepared_call)
            checks.append(self._check(
                "MCP project_files tool execution",
                invocation["passed"],
                invocation["error"] or "Tool execution and result contract validated.",
                invocation["durationSeconds"],
            ))
        else:
            checks.append(self._check(
                "MCP project_files tool execution",
                False,
                "Skipped because MCP health, registry, or required-tool validation failed.",
                skipped=True,
            ))
        return checks