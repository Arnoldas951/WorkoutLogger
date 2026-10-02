# 3.12 is a hard floor: garminconnect dropped 3.11 at 0.3.3.
FROM python:3.12-slim

# curl_cffi (pulled in by garminconnect) ships compiled wheels, but the TLS
# impersonation it does needs a real libcurl present at runtime.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY *.py ./

# Tokens live on a volume, not in the image. See README.
ENV GARMINTOKENS=/tokens
VOLUME ["/tokens"]

CMD ["python", "sync.py"]
