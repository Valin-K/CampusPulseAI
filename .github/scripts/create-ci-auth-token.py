import base64
import hashlib
import hmac
import json
import time


def encode(value: bytes) -> str:
    return base64.urlsafe_b64encode(value).rstrip(b"=").decode("ascii")


now = int(time.time())
header = {"alg": "HS256", "typ": "JWT"}
claims = {
    "iss": "CampusPulse.Dev",
    "aud": "CampusPulse.Api",
    "sub": "1",
    "user_id": "1",
    "role": "Student",
    "scope": "CampusPulse.Access",
    "nbf": now,
    "exp": now + 3600,
}
unsigned_token = ".".join(
    encode(json.dumps(value, separators=(",", ":")).encode("utf-8"))
    for value in (header, claims)
)
signing_key = b"campuspulse-demo-only-signing-key-change-before-any-use"
signature = hmac.new(signing_key, unsigned_token.encode("ascii"), hashlib.sha256).digest()
print(f"{unsigned_token}.{encode(signature)}")
