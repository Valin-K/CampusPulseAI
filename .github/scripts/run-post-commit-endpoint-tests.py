import argparse
import json
import os
import re
import subprocess
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from typing import Any, Callable
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
from urllib.parse import urlsplit


class EndpointTestRunner:
    def __init__(self, student: int, base_url: str, report_dir: Path) -> None:
        parsed_url = urlsplit(base_url)
        is_local_http = parsed_url.scheme == "http" and parsed_url.hostname in {
            "localhost",
            "127.0.0.1",
            "::1",
        }
        if (
            not parsed_url.hostname
            or parsed_url.username
            or parsed_url.password
            or (parsed_url.scheme != "https" and not is_local_http)
        ):
            raise ValueError("The service URL must use HTTPS or loopback HTTP.")
        self.student = student
        self.base_url = base_url.rstrip("/")
        self.report_dir = report_dir
        self.results: list[dict[str, Any]] = []
        self.auth_errors: list[str] = []
        self.run_id = os.environ.get("GITHUB_RUN_ID", "local")
        self.run_attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "1")
        self.commit_sha = os.environ.get("GITHUB_SHA", "local")
        self.branch = os.environ.get("GITHUB_REF_NAME", "local")
        self.workflow_url = ""
        server_url = os.environ.get("GITHUB_SERVER_URL")
        repository = os.environ.get("GITHUB_REPOSITORY")
        if server_url and repository and self.run_id != "local":
            self.workflow_url = f"{server_url}/{repository}/actions/runs/{self.run_id}"

    def token(self, account_id: str, role: str) -> str | None:
        auth_script = Path(__file__).with_name("create-ci-auth-token.py")
        environment = os.environ.copy()
        environment.setdefault("CAMPUSPULSE_AUTH_URL", "http://localhost:5020/api/auth/token")
        try:
            result = subprocess.run(  # noqa: S603 - fixed in-repository helper, argv list, shell disabled
                [
                    sys.executable,
                    str(auth_script),
                    "--account-id",
                    account_id,
                    "--role",
                    role,
                ],
                check=False,
                capture_output=True,
                text=True,
                timeout=70,
                env=environment,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            self.auth_errors.append(f"{role} token setup failed: {error}")
            return None

        token = result.stdout.strip()
        if result.returncode != 0 or not token:
            message = result.stderr.strip() or f"token helper exited with {result.returncode}"
            self.auth_errors.append(f"{role} token setup failed: {message}")
            return None
        return token

    def request(
        self,
        method: str,
        path: str,
        payload: dict[str, Any] | None = None,
        token: str | None = None,
    ) -> tuple[int, Any]:
        headers = {"Accept": "application/json"}
        data = None
        if payload is not None:
            data = json.dumps(payload).encode("utf-8")
            headers["Content-Type"] = "application/json"
        if token:
            headers["Authorization"] = f"Bearer {token}"

        request = Request(  # noqa: S310 - base URL scheme is restricted in the constructor
            f"{self.base_url}{path}",
            data=data,
            headers=headers,
            method=method,
        )
        try:
            with urlopen(request, timeout=15) as response:  # noqa: S310 - base URL scheme is restricted in the constructor
                status = response.status
                raw_body = response.read()
        except HTTPError as error:
            status = error.code
            raw_body = error.read()
        except (URLError, TimeoutError, ConnectionError) as error:
            raise RuntimeError(f"request failed: {error}") from error

        if not raw_body:
            return status, None
        try:
            return status, json.loads(raw_body.decode("utf-8"))
        except (UnicodeDecodeError, json.JSONDecodeError):
            return status, None

    def case(
        self,
        name: str,
        method: str,
        path: str,
        expected_status: int,
        payload: dict[str, Any] | None = None,
        token: str | None = None,
        requires_auth: bool = False,
        validate: Callable[[Any], str] | None = None,
    ) -> Any:
        status = 0
        try:
            if requires_auth and not token:
                raise RuntimeError("required authorization token is unavailable; see authentication diagnostics")
            status, body = self.request(method, path, payload, token)
            if status != expected_status:
                raise AssertionError(f"expected HTTP {expected_status}, received HTTP {status}")
            detail = validate(body) if validate else "response status matched"
            result = "PASS"
        except Exception as error:
            detail = " ".join(str(error).split()).replace("|", "\\|")
            result = "FAIL"

        self.results.append(
            {
                "student": self.student,
                "name": name,
                "method": method,
                "endpoint": path,
                "expected_status": expected_status,
                "actual_status": status,
                "result": result,
                "detail": detail,
                "timestamp_utc": datetime.now(timezone.utc).isoformat().replace("+00:00", "Z"),
                "commit_sha": self.commit_sha,
                "branch": self.branch,
                "workflow_run_url": self.workflow_url,
                "run_attempt": self.run_attempt,
            }
        )
        print(f"{result}: {method} {path} (HTTP {status}; {detail})")
        return body if result == "PASS" else None

    @staticmethod
    def require_object(body: Any) -> dict[str, Any]:
        if not isinstance(body, dict):
            raise AssertionError("expected a JSON object response")
        return body

    def run_student_1(self) -> None:
        staff_token = self.token("STF001", "Staff")
        run_suffix = re.sub(r"[^A-Za-z0-9]", "", self.run_id) or "local"
        email = f"ci.student.{run_suffix}.{self.run_attempt}@example.edu"

        def validate_enrollment(body: Any) -> str:
            student = self.require_object(body)
            if not isinstance(student.get("studentId"), str) or not student["studentId"]:
                raise AssertionError("response did not include a studentId")
            return "created student ID returned"

        def validate_student_read(body: Any) -> str:
            student = self.require_object(body)
            if student.get("studentId") != student_id or student.get("email") != email:
                raise AssertionError("response did not match the newly enrolled student")
            return "new student profile matches the enrolment"

        enrollment = self.case(
            "Student enrolment",
            "POST",
            "/api/students/enroll",
            201,
            {
                "firstName": "CI",
                "lastName": "Student",
                "email": email,
                "dateOfBirth": "2000-01-01",
                "mailingAddress": "1 CI Way",
                "emergencyContactName": "CI Contact",
                "emergencyContactPhone": "0400000000",
                "phoneNumber": "0400000001",
                "degree": "Bachelor of Testing",
                "major": "Software Engineering",
                "password": "StrongPassword!2026",
                "confirmPassword": "StrongPassword!2026",
            },
            validate=validate_enrollment,
        )
        student_id = (
            enrollment.get("studentId")
            if isinstance(enrollment, dict)
            else "CI-POST-FAILED"
        )
        self.case(
            "New student profile read",
            "GET",
            f"/api/students/{student_id}",
            200,
            token=staff_token,
            requires_auth=True,
            validate=validate_student_read,
        )

    def run_student_2(self) -> None:
        staff_token = self.token("STF001", "Staff")
        student_token = self.token("STU001", "Student")
        run_suffix = re.sub(r"[^A-Za-z0-9]", "", self.run_id)[-12:] or "local"
        course_code = f"CI{run_suffix}{self.run_attempt}".upper()

        def validate_course(body: Any) -> str:
            course = self.require_object(body)
            if (
                not isinstance(course.get("id"), int)
                or course["id"] <= 0
                or course.get("code") != course_code
            ):
                raise AssertionError("response did not identify the created course")
            return "created course ID and code returned"

        def validate_enrollment(body: Any) -> str:
            summary = self.require_object(body)
            enrollments = summary.get("enrollments")
            if not isinstance(enrollments, list) or not any(
                item.get("courseCode") == course_code for item in enrollments if isinstance(item, dict)
            ):
                raise AssertionError("response did not include the new course enrolment")
            return "enrolment summary contains the created course"

        self.case(
            "Course creation",
            "POST",
            "/api/courses",
            201,
            {
                "code": course_code,
                "name": "CI Test Course",
                "description": "Created by the post-commit endpoint test.",
                "creditPoints": 6,
                "level": 1,
                "isActive": True,
            },
            token=staff_token,
            requires_auth=True,
            validate=validate_course,
        )
        self.case(
            "Student course enrolment",
            "POST",
            "/api/course-access/student/enrollments",
            201,
            {"courseCode": course_code, "completedCourses": []},
            token=student_token,
            requires_auth=True,
            validate=validate_enrollment,
        )

    def run_student_3(self) -> None:
        student_token = self.token("STU001", "Student")

        def validate_room_list(body: Any) -> str:
            if not isinstance(body, list) or not body:
                raise AssertionError("room list was empty or not a JSON array")
            return f"received {len(body)} rooms"

        def validate_room_detail(body: Any) -> str:
            room = self.require_object(body)
            if room.get("id") != 1:
                raise AssertionError("response did not contain room ID 1")
            return "seeded room 1 returned"

        self.case(
            "Housing room list",
            "GET",
            "/api/housing/rooms",
            200,
            token=student_token,
            requires_auth=True,
            validate=validate_room_list,
        )
        self.case(
            "Housing room detail",
            "GET",
            "/api/housing/rooms/1",
            200,
            token=student_token,
            requires_auth=True,
            validate=validate_room_detail,
        )

    def run_student_4(self) -> None:
        staff_token = self.token("STF001", "Staff")
        issue_date = date.today()
        due_date = issue_date + timedelta(days=30)
        description = f"CI invoice {self.run_id}"
        updated_description = f"Updated CI invoice {self.run_id}"

        def validate_invoice(body: Any) -> str:
            invoice = self.require_object(body)
            if (
                not isinstance(invoice.get("id"), int)
                or invoice["id"] <= 0
                or invoice.get("description") != description
            ):
                raise AssertionError("response did not identify the created invoice")
            return "created invoice ID and description returned"

        def validate_invoice_update(body: Any) -> str:
            invoice = self.require_object(body)
            if (
                invoice.get("id") != invoice_id
                or invoice.get("description") != updated_description
                or invoice.get("amount") != 4500
            ):
                raise AssertionError("response did not reflect the invoice update")
            return "updated invoice data returned"

        created_invoice = self.case(
            "Invoice creation",
            "POST",
            "/api/invoices",
            201,
            {
                "studentId": 4,
                "description": description,
                "amount": 1250.0,
                "issueDate": issue_date.isoformat(),
                "dueDate": due_date.isoformat(),
                "status": 0,
            },
            token=staff_token,
            requires_auth=True,
            validate=validate_invoice,
        )
        invoice_id = (
            created_invoice.get("id")
            if isinstance(created_invoice, dict)
            else 2_147_483_647
        )
        self.case(
            "Invoice update",
            "PUT",
            f"/api/invoices/{invoice_id}",
            200,
            {
                "studentId": 4,
                "description": updated_description,
                "amount": 4500.0,
                "issueDate": issue_date.isoformat(),
                "dueDate": due_date.isoformat(),
                "status": 1,
            },
            token=staff_token,
            requires_auth=True,
            validate=validate_invoice_update,
        )

    def run_student_5(self) -> None:
        student_token = self.token("STU005", "Student")
        start = datetime.now(timezone.utc).replace(second=0, microsecond=0) + timedelta(days=30)
        end = start + timedelta(hours=1)
        resource_name = f"CI booking {self.run_id}"
        updated_resource_name = f"Updated CI booking {self.run_id}"

        def validate_booking(body: Any) -> str:
            booking = self.require_object(body)
            if (
                not isinstance(booking.get("id"), int)
                or booking["id"] <= 0
                or booking.get("resourceName") != resource_name
            ):
                raise AssertionError("response did not identify the created booking")
            return "created booking ID and resource name returned"

        def validate_booking_update(body: Any) -> str:
            booking = self.require_object(body)
            if booking.get("id") != booking_id or booking.get("resourceName") != updated_resource_name:
                raise AssertionError("response did not reflect the booking update")
            return "updated booking data returned"

        created_booking = self.case(
            "Booking creation",
            "POST",
            "/api/bookings",
            201,
            {
                "studentId": 5,
                "roomId": 2,
                "refEquipmentId": None,
                "resourceName": resource_name,
                "startUtc": start.isoformat().replace("+00:00", "Z"),
                "endUtc": end.isoformat().replace("+00:00", "Z"),
                "status": 1,
            },
            token=student_token,
            requires_auth=True,
            validate=validate_booking,
        )
        booking_id = (
            created_booking.get("id")
            if isinstance(created_booking, dict)
            else 2_147_483_647
        )
        self.case(
            "Booking update",
            "PUT",
            f"/api/bookings/{booking_id}",
            200,
            {
                "studentId": 5,
                "roomId": 2,
                "refEquipmentId": None,
                "resourceName": updated_resource_name,
                "startUtc": start.isoformat().replace("+00:00", "Z"),
                "endUtc": end.isoformat().replace("+00:00", "Z"),
                "status": 1,
            },
            token=student_token,
            requires_auth=True,
            validate=validate_booking_update,
        )

    def write_report(self) -> bool:
        self.report_dir.mkdir(parents=True, exist_ok=True)
        passed = sum(result["result"] == "PASS" for result in self.results)
        total = len(self.results)
        outcome = "PASS" if passed == total and total == 2 else "FAIL"
        report_path = self.report_dir / f"student-{self.student}-endpoint-results.md"
        audit_path = self.report_dir / f"student-{self.student}-endpoint-audit.jsonl"
        run_link = f"[{self.run_id}]({self.workflow_url})" if self.workflow_url else self.run_id
        lines = [
            f"# Student {self.student} post-commit CI/CD endpoint test report",
            "",
            f"- Overall result: **{outcome}** ({passed}/{total} endpoint tests passed)",
            f"- Commit: `{self.commit_sha}`",
            f"- Branch: `{self.branch}`",
            f"- Workflow run: {run_link} (attempt {self.run_attempt})",
            f"- Generated (UTC): {datetime.now(timezone.utc).isoformat().replace('+00:00', 'Z')}",
            "",
            "| Endpoint test | Method | Endpoint | Expected HTTP | Actual HTTP | Result | Details |",
            "|---|---|---|---:|---:|---|---|",
        ]
        for result in self.results:
            lines.append(
                f"| {result['name']} | {result['method']} | `{result['endpoint']}` "
                f"| {result['expected_status']} | {result['actual_status']} "
                f"| **{result['result']}** | {result['detail']} |"
            )
        if self.auth_errors:
            lines.extend(["", "### Authentication setup diagnostics", ""])
            lines.extend(f"- {error}" for error in self.auth_errors)
        report = "\n".join(lines) + "\n"
        report_path.write_text(report, encoding="utf-8")
        audit_path.write_text(
            "".join(json.dumps(result, ensure_ascii=True) + "\n" for result in self.results),
            encoding="utf-8",
        )

        summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
        if summary_path:
            with Path(summary_path).open("a", encoding="utf-8") as summary:
                summary.write(report)
        print(f"Endpoint test report: {report_path}")
        print(f"Endpoint test audit: {audit_path}")
        return outcome == "PASS"

    def run(self) -> bool:
        test_by_student = {
            1: self.run_student_1,
            2: self.run_student_2,
            3: self.run_student_3,
            4: self.run_student_4,
            5: self.run_student_5,
        }
        test_by_student[self.student]()
        return self.write_report()


def main() -> int:
    parser = argparse.ArgumentParser(description="Run two post-commit endpoint tests for a student service.")
    parser.add_argument("--student", type=int, choices=range(1, 6), required=True)
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--report-dir", default="reports/post-commit")
    arguments = parser.parse_args()

    runner = EndpointTestRunner(arguments.student, arguments.base_url, Path(arguments.report_dir))
    return 0 if runner.run() else 1


if __name__ == "__main__":
    sys.exit(main())
