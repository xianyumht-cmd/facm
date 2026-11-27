"""Publish only the signed control object; confirm anonymous download byte-for-byte."""
import hashlib
import os
import pathlib
import sys
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
client.put_object(Bucket=bucket, Key="control.json", Body=body, ACL="public-read",
                  ContentType="application/json", CacheControl="no-cache, max-age=0",
                  EnableMD5=True)
request = urllib.request.Request(url, headers={"Cache-Control": "no-cache", "User-Agent": "GGman-control-publish/1"})
with urllib.request.urlopen(request, timeout=15) as response:
    received = response.read(65537)
    if response.status != 200 or received != body:
        raise SystemExit("Anonymous COS readback does not match uploaded control envelope")
print("Verified anonymous HTTPS GET; bytes=%d; sha256=%s" % (len(body), hashlib.sha256(body).hexdigest()))
