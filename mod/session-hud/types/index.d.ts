export type HudMeasure = {
  model: string
  contextTokens: number | null
  contextWindow: number
  contextPercent: number | null
  /** 자동 압축이 일어나는 토큰 수(꺼져 있으면 컨텍스트 창 크기) */
  compactLimit: number
  /** contextTokens / compactLimit: 자동 압축까지 얼마나 찼는지 */
  compactPercent: number | null
  costUsd: number | null
  weeklyPercent: number | null
  weeklyResetsAt: string | null
  fiveHourPercent: number | null
  fiveHourResetsAt: string | null
}

declare module 'claude-code' {
  interface PluginState {
    'session-hud': {
      measure: HudMeasure | null
      isFavorite: boolean
      isCompacting: boolean
    }
  }
}
