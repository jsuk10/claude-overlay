import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { HudMeasure } from '../types'

import { METER, meterCells, meterColor } from './meter'

// 데스크톱 오버레이(ClaudeSessionOverlay)가 읽는 곳: %USERPROFILE%\.claude\session-hud\
//   sessions\<sessionId>.json  세션 하나의 현재 상태(이 Mod가 씀)
//   favorites.json             즐겨찾기 세션 id 목록(Mod와 오버레이가 함께 씀)

const measureAtom = atom({ plugin: 'session-hud', key: 'measure' } as const, null)
const favoriteAtom = atom({ plugin: 'session-hud', key: 'isFavorite' } as const, false)
const compactingAtom = atom({ plugin: 'session-hud', key: 'isCompacting' } as const, false)

type Status = 'working' | 'waiting' | 'idle'

type ShellTask = {
  id: string
  kind: 'shell'
  description: string
  status: string
  startedAt: number
}

const HEARTBEAT_MS = 10_000
const REQUEST_POLL_MS = 2_000

const shortModel = (model: string) =>
  model.replace(/^claude-/, '').replace(/\[1m\]$/, ' 1M')

const kilo = (n: number) => (n >= 1_000_000 ? `${(n / 1_000_000).toFixed(1)}M` : `${Math.round(n / 1000)}k`)

const firstLine = (text: string, max: number) => {
  const line = text.replace(/\s+/g, ' ').trim()
  return line.length > max ? `${line.slice(0, max - 1)}…` : line
}

type CompactRequest = {
  action: string
  requestedAt: number
  handledAt?: number
}

type HudState = {
  dataDir: string
  sessionId: string
  pid: number | null
  cwd: string
  title: string
  lastPrompt: string
  status: Status
  startedAt: number
  turnStartedAt: number | null
  lastCompletedAt: number | null
  seenAt: number | null
  measure: HudMeasure | null
  shells: Map<string, ShellTask>
  isContextWarned: boolean
  isHandlingRequest: boolean
  compactLimit: number
  compactLimitKey: string
}

// 모듈 변수: 리로드되면 새로 시작한다(세션이 다시 시작될 때 session.start가 채움)
const s: HudState = {
  dataDir: '',
  sessionId: '',
  pid: null,
  cwd: '',
  title: '',
  lastPrompt: '',
  status: 'idle',
  startedAt: 0,
  turnStartedAt: null,
  lastCompletedAt: null,
  seenAt: null,
  measure: null,
  shells: new Map(),
  isContextWarned: false,
  isHandlingRequest: false,
  compactLimit: 0,
  compactLimitKey: '',
}

const sessionFile = (id: string) => `${s.dataDir}\\sessions\\${id}.json`
const favoritesFile = () => `${s.dataDir}\\favorites.json`

async function readFavorites($: EngineInterface): Promise<string[]> {
  try {
    const parsed = JSON.parse(await $.fs.read(favoritesFile()))
    return Array.isArray(parsed?.ids) ? parsed.ids.filter((x: unknown) => typeof x === 'string') : []
  } catch {
    return []
  }
}

async function syncFavorite($: EngineInterface) {
  const isFavorite = (await readFavorites($)).includes(s.sessionId)
  if ((await read($, favoriteAtom)) !== isFavorite) await update($, favoriteAtom, () => isFavorite)
}

async function refreshMeasure($: EngineInterface) {
  const [usage, model] = await Promise.all([$.session.usage(), $.session.model()])
  const weekly = usage.rateLimits.find(r => r.kind === 'seven_day')
  const fiveHour = usage.rateLimits.find(r => r.kind === 'five_hour')
  const contextPercent = usage.context.percent ?? null
  const contextTokens = usage.context.tokens ?? null

  // 자동 압축 기준은 모델·창이 바뀔 때만 다시 센다(summary는 로컬 추정이라 요청을 보내지 않는다)
  const limitKey = `${model}|${usage.context.window}`
  if (s.compactLimitKey !== limitKey) {
    const detailed = await $.session.usage({ breakdown: 'summary', columns: 80 })
    const breakdown = detailed.context.breakdown
    s.compactLimit = breakdown?.autoCompactThreshold ?? breakdown?.rawMaxTokens ?? usage.context.window
    s.compactLimitKey = limitKey
  }
  const compactLimit = s.compactLimit > 0 ? s.compactLimit : usage.context.window
  const compactPercent = contextTokens === null ? null : Math.round((contextTokens / compactLimit) * 100)

  s.measure = {
    model,
    contextTokens,
    contextWindow: usage.context.window,
    contextPercent,
    compactLimit,
    compactPercent,
    costUsd: usage.cost?.usd ?? null,
    weeklyPercent: weekly?.percentUsed ?? null,
    weeklyResetsAt: weekly?.resetsAt ?? null,
    fiveHourPercent: fiveHour?.percentUsed ?? null,
    fiveHourResetsAt: fiveHour?.resetsAt ?? null,
  }
  await update($, measureAtom, () => s.measure)

  // 컨텍스트가 위험 구간에 들어서는 순간 한 번 알린다(압축 등으로 내려가면 다시 알릴 수 있게 된다)
  if (compactPercent !== null) {
    if (compactPercent >= METER.criticalAt && !s.isContextWarned) {
      s.isContextWarned = true
      $.ui.toast(`컨텍스트가 자동 압축 기준의 ${compactPercent}%예요 — ⇣압축을 눌러 정리하는 걸 권해요`)
    } else if (compactPercent < METER.warnAt) {
      s.isContextWarned = false
    }
  }
}

// 오버레이가 남긴 압축 요청(requests\<id>.json)을 처리한다. 턴이 도는 중이면 끝난 뒤 다시 시도한다
async function handleRequests($: EngineInterface) {
  if (s.dataDir === '' || s.sessionId === '' || s.isHandlingRequest) return
  const file = `${s.dataDir}\\requests\\${s.sessionId}.json`
  if (!(await $.fs.exists(file))) return
  s.isHandlingRequest = true
  try {
    const request = JSON.parse(await $.fs.read(file)) as CompactRequest
    if (request.action !== 'compact' || (request.handledAt ?? 0) >= request.requestedAt) return
    if (s.status !== 'idle') return
    let result = 'compacted'
    try {
      const done = await $.session.compact()
      if ('skip' in done && done.skip !== undefined) result = `skipped: ${done.skip}`
    } catch {
      return // 턴이 막 시작됐다: 다음 확인 때 다시
    }
    const handledAt = await $.clock.now()
    await $.fs.write(file, JSON.stringify({ ...request, handledAt, result }))
    $.ui.toast(result === 'compacted' ? '오버레이에서 요청한 컨텍스트 압축을 마쳤어요' : `압축 건너뜀: ${result}`)
    await refreshMeasure($).catch(() => undefined)
    await publish($)
  } catch {
    // 요청 파일이 깨졌거나 읽는 중에 바뀌었다: 다음 확인 때 다시
  } finally {
    s.isHandlingRequest = false
  }
}

async function publish($: EngineInterface) {
  if (s.dataDir === '' || s.sessionId === '') return
  try {
    await writeStatus($)
  } catch {
    // 상태 파일 쓰기 실패는 세션 진행을 막지 않는다
  }
}

async function writeStatus($: EngineInterface) {
  const agents = await $.agent.list().catch(() => [])
  const doc = {
    schema: 2, // 2: 오버레이의 압축 요청(requests\<id>.json)을 처리한다
    sessionId: s.sessionId,
    pid: s.pid,
    cwd: s.cwd,
    title: s.title,
    lastPrompt: s.lastPrompt,
    status: s.status,
    startedAt: s.startedAt,
    turnStartedAt: s.turnStartedAt,
    lastCompletedAt: s.lastCompletedAt,
    seenAt: s.seenAt,
    updatedAt: await $.clock.now(),
    surfaces: await $.session.surfaces(),
    measure: s.measure,
    tasks: [
      ...agents.map(a => ({
        id: a.id,
        kind: 'agent',
        description: a.description || a.name || a.type,
        type: a.type,
        status: a.status,
        parentId: a.parentId ?? null,
      })),
      ...s.shells.values(),
    ],
  }
  await $.fs.write(sessionFile(s.sessionId), JSON.stringify(doc, null, 2))
}

async function setStatus($: EngineInterface, next: Status) {
  s.status = next
  await publish($)
}

// /clear 등으로 세션 id가 바뀌면 이전 파일을 지우고 새 id로 다시 쓴다
async function followSessionId($: EngineInterface) {
  const id = await $.session.id()
  if (id === s.sessionId) return
  if (s.sessionId !== '') await $.fs.write(sessionFile(s.sessionId), JSON.stringify({ schema: 2, sessionId: s.sessionId, ended: true }))
  s.sessionId = id
  s.title = ''
  s.shells.clear()
  await syncFavorite($)
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    const started = await next(e)
    const home = (await $.env.get('USERPROFILE')) ?? (await $.env.get('HOME')) ?? ''
    s.dataDir = `${home}\\.claude\\session-hud`
    s.sessionId = await $.session.id()
    s.cwd = e.cwd
    s.startedAt = await $.clock.now()

    const firstUser = (await $.session.messages()).find(m => m.role === 'user' && m.text.trim() !== '')
    if (firstUser !== undefined) s.title = firstLine(firstUser.text, 80)

    await syncFavorite($)
    await refreshMeasure($).catch(() => undefined)
    await publish($)

    // 이 세션을 돌리는 claude 프로세스 pid: 자식 프로세스의 부모가 곧 이 프로세스다
    void (async () => {
      const ran = await $.process.run(
        ['powershell', '-NoProfile', '-Command', '(Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId'],
        { timeoutMs: 20_000 },
      )
      const parsed = Number(ran.stdout.trim())
      if (Number.isInteger(parsed) && parsed > 0) {
        s.pid = parsed
        await publish($)
      }
    })().catch(() => undefined)

    $.clock.every(REQUEST_POLL_MS, () => handleRequests($))

    $.clock.every(HEARTBEAT_MS, async () => {
      await followSessionId($)
      await syncFavorite($)
      s.cwd = await $.session.cwd()
      await publish($)
    })

    return started
  })

  on('session.end', async ($, e, next) => {
    if (s.dataDir !== '' && s.sessionId !== '') {
      await $.fs.write(sessionFile(s.sessionId), JSON.stringify({ schema: 2, sessionId: s.sessionId, ended: true })).catch(() => undefined)
    }
    return next(e)
  }).catch(($, e, next) => next(e))

  on('prompt.submit', async ($, e, next) => {
    const now = await $.clock.now()
    s.seenAt = now
    if (e.text.trim() !== '') {
      s.lastPrompt = firstLine(e.text, 120)
      if (s.title === '') s.title = firstLine(e.text, 80)
    }
    return next(e)
  }).catch(($, e, next) => next(e))

  on('turn.start', async ($, e, next) => {
    if (e.agentId === undefined) {
      await followSessionId($)
      s.turnStartedAt = await $.clock.now()
      await setStatus($, 'working')
    } else {
      await publish($)
    }
    return next(e)
  }).catch(($, e, next) => next(e))

  on('turn.complete', async ($, e, next) => {
    const done = await next(e)
    if (e.agentId === undefined) {
      s.lastCompletedAt = await $.clock.now()
      s.turnStartedAt = null
      await refreshMeasure($).catch(() => undefined)
      await setStatus($, 'idle')
    } else {
      await publish($)
    }
    return done
  }).catch(($, e, next) => next(e))

  on('session.measure', async ($, e, next) => {
    await refreshMeasure($).catch(() => undefined)
    await publish($)
    return next(e)
  }).catch(($, e, next) => next(e))

  // 권한 확인 대화상자가 떠 있는 동안은 '입력 대기'
  on('classic.PermissionRequest', async ($, e, next) => {
    if (s.status === 'working') await setStatus($, 'waiting')
    return next(e)
  }).catch(($, e, next) => next(e))

  on('tool.call', async ($, e, next) => {
    const isQuestion = e.tool === 'AskUserQuestion' && e.agentId === undefined
    if (isQuestion) await setStatus($, 'waiting')

    const ran = await next(e)

    if (s.status === 'waiting' && e.agentId === undefined) await setStatus($, 'working')

    if (e.tool === 'Bash' && ran.deny === undefined && ran.isError !== true) {
      const taskId = (ran.result as { backgroundTaskId?: unknown } | undefined)?.backgroundTaskId
      if (typeof taskId === 'string' && taskId !== '') {
        s.shells.set(taskId, {
          id: taskId,
          kind: 'shell',
          description: firstLine(e.description ?? e.command, 60),
          status: 'running',
          startedAt: await $.clock.now(),
        })
        await publish($)
      }
    }
    return ran
  }).catch(($, e, next) => next(e))

  // 백그라운드 셸이 끝났다는 알림(<task-notification>)을 보고 상태를 갱신한다
  on('session.append', async ($, e, next) => {
    const stored = await next(e)
    if (s.shells.size > 0 && e.message.role === 'user' && Array.isArray(e.message.content)) {
      for (const block of e.message.content) {
        if (block.type !== 'text' || typeof block.text !== 'string') continue
        const id = /<task-id>([^<]+)<\/task-id>/.exec(block.text)?.[1]
        const ended = /<status>([^<]+)<\/status>/.exec(block.text)?.[1]
        const shell = id === undefined ? undefined : s.shells.get(id.trim())
        if (shell !== undefined && ended !== undefined) {
          shell.status = ended.trim()
          await publish($)
        }
      }
    }
    return stored
  }).catch(($, e, next) => next(e))

  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    if (e.props.hasSurvey || e.props.view.agentId !== undefined) return next(e)

    const m = await read($, measureAtom)
    const isFavorite = await read($, favoriteAtom)
    const isCompacting = await read($, compactingAtom)
    const isWorking = e.props.isWorking
    const { Box, Text, Button } = $.ui.resolve(e)

    const model = m === null ? '…' : shortModel(m.model)
    // 컨텍스트는 자동 압축 기준으로 잰다: 100%가 되면 압축된다
    const ctxPercent = m?.compactPercent ?? null
    const ctxAmount =
      m === null ? '' : m.contextTokens === null ? `—/${kilo(m.compactLimit)}` : `${kilo(m.contextTokens)}/${kilo(m.compactLimit)}`
    const cost = m?.costUsd == null ? '$—' : `$${m.costUsd.toFixed(2)}`
    const weeklyPercent = m?.weeklyPercent ?? null

    const meter = (key: string, label: string, percent: number | null, cells: number) => {
      const { filled, empty } = meterCells(percent ?? 0, cells)
      const color = meterColor(percent)
      return (
        <Box key={key} flexDirection="row">
          <Text dimColor>{label} </Text>
          <Text color={color} backgroundColor={METER.track}>
            {filled}
          </Text>
          <Text backgroundColor={METER.track}>{empty}</Text>
          <Text color={color}> {percent === null ? '—' : `${Math.round(percent)}%`}</Text>
        </Box>
      )
    }

    const compact = async () => {
      if (await read($, compactingAtom)) return
      await update($, compactingAtom, () => true)
      try {
        const done = await $.session.compact()
        if ('skip' in done && done.skip !== undefined) $.ui.toast(`압축 건너뜀: ${done.skip}`)
        else $.ui.toast('컨텍스트를 압축했어요')
      } catch {
        $.ui.toast('작업 중에는 압축할 수 없어요. 턴이 끝난 뒤 다시 눌러주세요')
      } finally {
        await update($, compactingAtom, () => false)
        await refreshMeasure($).catch(() => undefined)
        await publish($)
      }
    }

    const toggleFavorite = async () => {
      const ids = await readFavorites($)
      const isOn = !ids.includes(s.sessionId)
      const nextIds = isOn ? [...ids, s.sessionId] : ids.filter(id => id !== s.sessionId)
      await $.fs.write(favoritesFile(), JSON.stringify({ ids: nextIds }, null, 2))
      await update($, favoriteAtom, () => isOn)
      $.ui.toast(isOn ? '즐겨찾기에 추가했어요 — 오버레이에 항상 표시됩니다' : '즐겨찾기를 해제했어요')
    }

    return (
      <Box flexDirection="row" width={e.props.bodyColumns} columnGap={2}>
        <Text bold color="claude" wrap="truncate-end">
          {model}
        </Text>
        {meter('session-hud-ctx', 'ctx', ctxPercent, 10)}
        <Text dimColor>{ctxAmount}</Text>
        <Button
          key="session-hud-compact"
          plain
          dimColor={isWorking || isCompacting}
          label={isCompacting ? '압축 중…' : '⇣압축'}
          onPress={() => void compact()}
        />
        <Text>{cost}</Text>
        <Box flexGrow={1} />
        {meter('session-hud-weekly', '주간', weeklyPercent, 8)}
        <Button key="session-hud-favorite" plain label={isFavorite ? '★' : '☆'} onPress={() => void toggleFavorite()} />
      </Box>
    )
  })
}
