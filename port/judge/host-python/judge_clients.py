"""Judge implementations of the upstream client contracts, and the Neo4j recording proxy.

These classes sit at the seams that the judge design names. They add no behavior to upstream
code: they record what crosses the seam and return scripted or computed values.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import math
import re
import time
import typing
from datetime import datetime, timedelta, timezone
from pathlib import Path

from codec import encode
from pydantic import BaseModel

from graphiti_core.cross_encoder.client import CrossEncoderClient
from graphiti_core.embedder.client import EmbedderClient
from graphiti_core.llm_client.client import LLMClient
from graphiti_core.llm_client.config import DEFAULT_MAX_TOKENS, LLMConfig, ModelSize
from graphiti_core.prompts.models import Message

EMBEDDING_DIM = 1024
UUID_RE = re.compile(
    r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f])'
)
ISO_RE = re.compile(r'\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?')


class Recorder:
    """Holds what one step sent across the seams."""

    def __init__(self) -> None:
        self.started = datetime.now(timezone.utc)
        self.recording_failed: list[str] = []
        self.reset()

    def reset(self) -> dict[str, list]:
        self.llm: list = []
        self.embed: list = []
        self.rerank: list = []
        self.queries: list = []
        self.missing: list = []
        return self.take()

    def take(self) -> dict[str, list]:
        return {
            'llm': self.llm,
            'embed': self.embed,
            'rerank': self.rerank,
            'queries': [q for q in self.queries if q.get('ops') or q.get('q')],
        }

    def mask(self, text: str) -> str:
        """Masks the noise that reply keys must ignore: UUIDs and clock values of this run."""
        low = self.started - timedelta(seconds=2)

        def swap(match: re.Match) -> str:
            try:
                value = datetime.fromisoformat(match.group(0).replace('Z', '+00:00').replace(' ', 'T'))
            except ValueError:
                return match.group(0)
            if value.tzinfo is None:
                value = value.replace(tzinfo=timezone.utc)
            return '<now>' if value >= low else match.group(0)

        return ISO_RE.sub(swap, UUID_RE.sub('<uuid>', text))


# --------------------------------------------------------------------------- embedder


def _token_vector(token: bytes) -> list[float]:
    seed = hashlib.sha256(token).digest()
    out = []
    for index in range(EMBEDDING_DIM):
        block = hashlib.sha256(seed + index.to_bytes(4, 'big')).digest()
        bits = int.from_bytes(block[:8], 'big') >> 11
        out.append(bits / 9007199254740992.0 * 2.0 - 1.0)
    return out


def _tokens(text: str) -> list[bytes]:
    """Runs of ASCII letters, ASCII digits, and bytes over 0x7F. ASCII letters are lowercased."""
    tokens: list[bytes] = []
    current = bytearray()
    for byte in text.encode('utf-8'):
        if 0x41 <= byte <= 0x5A:
            current.append(byte + 0x20)
        elif 0x61 <= byte <= 0x7A or 0x30 <= byte <= 0x39 or byte >= 0x80:
            current.append(byte)
        elif current:
            tokens.append(bytes(current))
            current = bytearray()
    if current:
        tokens.append(bytes(current))
    return tokens


_VECTOR_CACHE: dict[bytes, list[float]] = {}


def judge_embedding(text: str) -> list[float]:
    """A deterministic bag-of-words embedding. Texts that share words get similar vectors.

    The C# host implements the same steps in the same order, so both produce the same doubles.
    """
    total = [0.0] * EMBEDDING_DIM
    for token in _tokens(text) or [b'']:
        vector = _VECTOR_CACHE.get(token)
        if vector is None:
            vector = _VECTOR_CACHE[token] = _token_vector(token)
        for index in range(EMBEDDING_DIM):
            total[index] += vector[index]
    norm = 0.0
    for value in total:
        norm += value * value
    norm = math.sqrt(norm)
    if norm == 0.0:
        return total
    return [value / norm for value in total]


class JudgeEmbedder(EmbedderClient):
    def __init__(self, recorder: Recorder):
        self.recorder = recorder

    async def create(self, input_data) -> list[float]:  # type: ignore[override]
        self.recorder.embed.append(encode(input_data))
        text = input_data if isinstance(input_data, str) else ' '.join(str(item) for item in input_data)
        return judge_embedding(text)

    async def create_batch(self, input_data_list: list[str]) -> list[list[float]]:
        self.recorder.embed.append({'batch': encode(input_data_list)})
        return [judge_embedding(text) for text in input_data_list]


class JudgeCrossEncoder(CrossEncoderClient):
    """Scores a passage by the cosine of the judge embeddings, mapped to the range 0 to 1."""

    def __init__(self, recorder: Recorder):
        self.recorder = recorder

    async def rank(self, query: str, passages: list[str]) -> list[tuple[str, float]]:
        self.recorder.rerank.append({'query': query, 'passages': list(passages)})
        query_vector = judge_embedding(query)
        scored = []
        for index, passage in enumerate(passages):
            vector = judge_embedding(passage)
            dot = 0.0
            for left, right in zip(query_vector, vector):
                dot += left * right
            scored.append((index, passage, (dot + 1.0) / 2.0))
        scored.sort(key=lambda item: (-item[2], item[0]))
        return [(passage, score) for _, passage, score in scored]


# --------------------------------------------------------------------------- LLM


class JudgeMissingReply(Exception):
    pass


class JudgeLLMClient(LLMClient):
    """Implements the provider contract `_generate_response` with scripted replies.

    With `inner` set, a request that has no stored reply goes to the real client one time and
    the reply is appended to the reply file.
    """

    def __init__(self, recorder: Recorder, replies_path: Path | None, inner: LLMClient | None = None):
        super().__init__(LLMConfig(api_key='judge', model='judge-medium', small_model='judge-small'), cache=False)
        self.recorder = recorder
        self.replies_path = replies_path
        self.inner = inner
        self.replies: dict[str, dict] = {}
        self._pending: dict[str, asyncio.Future] = {}
        if replies_path is not None and replies_path.exists():
            for line in replies_path.read_text(encoding='utf-8').split('\n'):
                if line.strip():
                    row = json.loads(line)
                    self.replies[row['key']] = row

    def _key(self, messages: list[Message], response_model: type[BaseModel] | None) -> tuple[str, list]:
        masked = [[m.role, self.recorder.mask(m.content)] for m in messages]
        name = response_model.__name__ if response_model is not None else None
        text = json.dumps([name, masked], ensure_ascii=False, separators=(',', ':'))
        return hashlib.sha256(text.encode('utf-8')).hexdigest(), masked

    async def _generate_response(
        self,
        messages: list[Message],
        response_model: type[BaseModel] | None = None,
        max_tokens: int = DEFAULT_MAX_TOKENS,
        model_size: ModelSize = ModelSize.medium,
        *,
        model: str | None = None,
    ) -> dict[str, typing.Any]:
        self.recorder.llm.append(
            {
                'messages': [{'role': m.role, 'content': m.content} for m in messages],
                'response_model': response_model.__name__ if response_model is not None else None,
                'max_tokens': max_tokens,
                'model_size': model_size.value,
                'model': model,
            }
        )
        key, masked = self._key(messages, response_model)
        row = self.replies.get(key)
        if row is not None:
            return json.loads(json.dumps(row['reply']))

        if self.inner is None or self.replies_path is None:
            self.recorder.missing.append(
                {'key': key, 'response_model': response_model.__name__ if response_model else None, 'messages': masked}
            )
            raise JudgeMissingReply(f'no stored reply for request {key[:12]}')

        # Two identical requests that are in flight together share one model call and one reply.
        pending = self._pending.get(key)
        if pending is not None:
            return json.loads(json.dumps(await pending))
        future: asyncio.Future = asyncio.get_running_loop().create_future()
        self._pending[key] = future
        started = time.monotonic()
        try:
            reply = await self.inner._generate_response(messages, response_model, max_tokens, model_size)
        except BaseException as error:
            # The runner refuses to write a golden result for a scenario with a failed recording.
            self.recorder.recording_failed.append(type(error).__name__)
            future.set_exception(error)
            del self._pending[key]
            raise
        future.set_result(reply)
        row = {
            'key': key,
            'response_model': response_model.__name__ if response_model else None,
            'messages': masked,
            'reply': reply,
            'recorded_with': getattr(self.inner, 'model', None),
            'seconds': round(time.monotonic() - started, 2),
        }
        self.replies[key] = row
        self.replies_path.parent.mkdir(parents=True, exist_ok=True)
        with self.replies_path.open('a', encoding='utf-8') as handle:
            handle.write(json.dumps(row, ensure_ascii=True) + '\n')
        return json.loads(json.dumps(reply))


# --------------------------------------------------------------------------- Neo4j


def _query_params(parameters: dict | None, kwargs: dict) -> dict:
    merged = dict(parameters or {})
    merged.update({k: v for k, v in kwargs.items() if not k.endswith('_')})
    return encode(merged)


class _RecordingTx:
    def __init__(self, tx: typing.Any, unit: dict):
        self._tx = tx
        self._unit = unit

    async def run(self, query: str, /, parameters: dict | None = None, **kwargs: typing.Any) -> typing.Any:
        self._unit['ops'].append({'q': str(query), 'params': _query_params(parameters, kwargs)})
        return await self._tx.run(query, parameters, **kwargs)

    def __getattr__(self, name: str) -> typing.Any:
        return getattr(self._tx, name)


class _RecordingSession:
    def __init__(self, session: typing.Any, recorder: Recorder):
        self._session = session
        self._unit: dict = {'kind': 'session', 'ops': []}
        recorder.queries.append(self._unit)

    async def __aenter__(self) -> '_RecordingSession':
        await self._session.__aenter__()
        return self

    async def __aexit__(self, *exc: typing.Any) -> typing.Any:
        return await self._session.__aexit__(*exc)

    async def run(self, query: str, /, parameters: dict | None = None, **kwargs: typing.Any) -> typing.Any:
        self._unit['ops'].append({'q': str(query), 'params': _query_params(parameters, kwargs)})
        return await self._session.run(query, parameters, **kwargs)

    async def begin_transaction(self, *args: typing.Any, **kwargs: typing.Any) -> _RecordingTx:
        return _RecordingTx(await self._session.begin_transaction(*args, **kwargs), self._unit)

    async def execute_write(self, work: typing.Any, *args: typing.Any, **kwargs: typing.Any) -> typing.Any:
        return await self._session.execute_write(
            lambda tx, *a, **k: work(_RecordingTx(tx, self._unit), *a, **k), *args, **kwargs
        )

    async def execute_read(self, work: typing.Any, *args: typing.Any, **kwargs: typing.Any) -> typing.Any:
        return await self._session.execute_read(
            lambda tx, *a, **k: work(_RecordingTx(tx, self._unit), *a, **k), *args, **kwargs
        )

    def __getattr__(self, name: str) -> typing.Any:
        return getattr(self._session, name)


class RecordingNeo4jClient:
    """Wraps the `neo4j` AsyncDriver that `Neo4jDriver.client` holds."""

    def __init__(self, client: typing.Any, recorder: Recorder):
        self._client = client
        self._recorder = recorder

    async def execute_query(self, query: str, /, parameters_: dict | None = None, **kwargs: typing.Any) -> typing.Any:
        options = {k: encode(v) for k, v in kwargs.items() if k.endswith('_') and k != 'database_'}
        unit = {'kind': 'query', 'q': str(query), 'params': _query_params(parameters_, kwargs)}
        if options:
            unit['options'] = options
        self._recorder.queries.append(unit)
        return await self._client.execute_query(query, parameters_=parameters_, **kwargs)

    def session(self, *args: typing.Any, **kwargs: typing.Any) -> _RecordingSession:
        return _RecordingSession(self._client.session(*args, **kwargs), self._recorder)

    def __getattr__(self, name: str) -> typing.Any:
        return getattr(self._client, name)
