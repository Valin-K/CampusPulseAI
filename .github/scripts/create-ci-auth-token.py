import argparse
import json
import os
import sys
import time
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


parser = argparse.ArgumentParser()
parser.add_argument("--account-id", required=True)
parser.add_argument("--role", choices=("Student", "Staff"), required=True)
arguments = parser.parse_args()

endpoint = os.environ.get(
    "CAMPUSPULSE_AUTH_URL",
    "http://localhost:5020/api/auth/token",
)
password = os.environ.get("CAMPUSPULSE_CI_PASSWORD", "test")
payload = json.dumps(
    {"accountId": arguments.account_id, "role": arguments.role, "password": password}
).encode("utf-8")
last_error = None

for attempt in range(30):
    request = Request(
        endpoint,
        data=payload,
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlopen(request, timeout=5) as response:
            token_response = json.load(response)
        access_token = token_response.get("accessToken")
        if not isinstance(access_token, str) or not access_token:
            raise SystemExit("The authentication endpoint returned no access token.")
        print(access_token)
        sys.exit(0)
    except HTTPError as error:
        if error.code < 500:
            raise SystemExit(f"Authentication failed with HTTP {error.code}.") from error
        last_error = error
    except (URLError, TimeoutError, json.JSONDecodeError) as error:
        last_error = error

    if attempt < 29:
        time.sleep(2)

raise SystemExit(f"The authentication endpoint did not become available: {last_error}")
