import { expect, test } from 'claude-code/testing'

import { METER, meterCells, meterColor } from '../hooks/meter'

const BAND = {
  component: 'AbovePrompt',
  props: {
    hasSurvey: false,
    isWorking: false,
    maxRows: 10,
    bodyColumns: 100,
    scroll: { offset: 0, bodyRows: 10 },
    view: {},
  },
} as const

test('한 줄 HUD에 컨텍스트·압축·주간·즐겨찾기가 모두 그려진다', async $ => {
  for (const surface of ['terminal', 'desktop'] as const) {
    const ui = await $.ui.mount({ plugin: 'session-hud', surface, ...BAND })
    expect(await ui.find({ type: 'Text', text: /^ctx/ })).toBeDefined()
    expect(await ui.find({ type: 'Text', text: /주간/ })).toBeDefined()
    expect(await ui.find({ key: 'session-hud-compact' })).toBeDefined()
    expect(await ui.find({ key: 'session-hud-favorite' })).toBeDefined()
    await ui.unmount()
  }
})

test('바는 칸을 8등분해 퍼센트만큼 채운다', () => {
  // 10칸 중 42% → 4칸 + 2/8칸(▎) + 빈 5칸
  expect(meterCells(42, 10)).toEqual({ filled: '████▎', empty: '     ' })
  // 8칸 중 95% → 7칸 + 5/8칸(▋)
  expect(meterCells(95, 8)).toEqual({ filled: '███████▋', empty: '' })
  expect(meterCells(0, 4)).toEqual({ filled: '', empty: '    ' })
  expect(meterCells(130, 4)).toEqual({ filled: '████', empty: '' })
})

test('기준값을 넘으면 색이 바뀐다', () => {
  expect(meterColor(null)).toBe(METER.normal)
  expect(meterColor(METER.warnAt - 1)).toBe(METER.normal)
  expect(meterColor(METER.warnAt)).toBe(METER.warn)
  expect(meterColor(METER.criticalAt)).toBe(METER.critical)
})

test('설문이 떠 있으면 밴드를 비운다', async ($, on) => {
  // 엔진 몫: 플러그인이 next(e)로 넘기면 이 트리가 그려진다
  on('ui.render', ($$, e) => {
    const { Text } = $$.ui.resolve(e)
    return <Text>engine</Text>
  })
  const ui = await $.ui.mount({
    plugin: 'session-hud',
    surface: 'terminal',
    ...BAND,
    props: { ...BAND.props, hasSurvey: true },
  })
  expect(await ui.find({ key: 'session-hud-favorite' })).toBeUndefined()
  await ui.unmount()
})
