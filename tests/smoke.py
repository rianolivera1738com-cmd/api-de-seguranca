import json
import os
import urllib.request
import urllib.error
import uuid

BASE = os.environ.get("API_BASE_URL", "http://127.0.0.1:5080").rstrip("/")

def request(path, data=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    req = urllib.request.Request(BASE + path, headers=headers,
        data=json.dumps(data).encode() if data is not None else None)
    try:
        with urllib.request.urlopen(req, timeout=30) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as error:
        return error.code, None

credentials = {"email": f"test-{uuid.uuid4().hex}@example.com", "password": "Teste-seguro-123!"}
assert request("/health")[0] == 200
assert request("/me")[0] == 401
assert request("/me", token="invalid")[0] == 401
assert request("/auth/register", {**credentials, "password": "curta"})[0] == 400
assert request("/auth/register", credentials)[0] == 201
assert request("/auth/register", credentials)[0] == 409
assert request("/auth/login", {**credentials, "password": "Senha-incorreta-123"})[0] == 401
status, body = request("/auth/login", credentials)
assert status == 200
status, profile = request("/me", token=body["access_token"])
assert status == 200 and profile["email"] == credentials["email"]
assert "password_hash" not in profile
print("OK: cadastro, duplicidade, login e rota protegida")
