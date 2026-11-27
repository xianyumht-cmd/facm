"""Publish only the signed control object; confirm anonymous download byte-for-byte."""
import base64
import hashlib
import json
import os
import pathlib
import sys
import urllib.error
import urllib.request
from qcloud_cos import CosConfig, CosS3Client

bucket = "ggman-config-1305353165"
url = "https://" + bucket + ".cos.ap-guangzhou.myqcloud.com/control.json"
body = pathlib.Path(sys.argv[1]).read_bytes()
if not body or len(body) > 65536:
    raise SystemExit("Invalid signed envelope size")
config = CosConfig(Region="ap-guangzhou", SecretId=os.environ["TENCENT_COS_SECRET_ID"],
                   SecretKey=os.environ["TENCENT_COS_SECRET_KEY"], Scheme="https")
client = CosS3Client(config)
request = urllib.request.Request(url, headers={"Cache-Control": "no-cache", "User-Agent": "GGman-control-publish/1"})

def identity(envelope):
    if not envelope or len(envelope) > 65536:
        raise ValueError("Invalid signed envelope size")
    data = json.loads(envelope)
    payload = base64.b64decode(data["payload"], validate=True)
    signature = base64.b64decode(data["signature"], validate=True)
    config = json.loads(payload.decode("utf-8-sig"))
    revision = config["revision"]
    if config["schema"] != "ggman-control.v1" or type(revision) is not int or revision <= 0 or not signature or len(payload) > 16384:
        raise ValueError("Invalid control identity")
    return revision, payload, signature

next_revision, next_payload, next_signature = identity(body)
try:
    with urllib.request.urlopen(request, timeout=15) as response:
        previous = response.read(65537)
except urllib.error.HTTPError as exc:
    if exc.code != 404:
        raise
    previous = None
if previous is not None:
    revision, payload, signature = identity(previous)
    if next_revision < revision or (next_revision == revision and (next_payload != payload or next_signature != signature)):
        raise SystemExit("Refusing older or conflicting COS control revision")
    if next_revision == revision:
        body = previous
if previous != body:
    client.put_object(Bucket=bucket, Key="control.json", Body=body, ACL="public-read",
                      ContentType="application/json", CacheControl="no-cache, max-age=0",
                      EnableMD5=True)
with urllib.request.urlopen(request, timeout=15) as response:
    received = response.read(65537)
    if response.status != 200 or received != body:
        raise SystemExit("Anonymous COS readback does not match uploaded control envelope")
print("Verified anonymous HTTPS GET; bytes=%d; sha256=%s" % (len(body), hashlib.sha256(body).hexdigest()))
