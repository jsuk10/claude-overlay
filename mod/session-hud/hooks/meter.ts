// 사용량 바 색: Claude 데스크톱 우하단 사용량 링과 같은 색과 기준
// (ion-dist의 --cds-fill-accent / --cds-fill-warning / --cds-fill-danger, 75%·90%에서 바뀜)
export const METER = {
  warnAt: 75,
  criticalAt: 90,
  normal: '#2A78D6',
  warn: '#FAB219',
  critical: '#D03B3B',
  // 데스크톱은 흰색 10%(어두운 테마) 트랙: 터미널은 알파가 없어 어두운 배경 위에 섞은 값을 쓴다
  track: '#3A3A3A',
} as const

export const meterColor = (percent: number | null) =>
  percent === null ? METER.normal : percent >= METER.criticalAt ? METER.critical : percent >= METER.warnAt ? METER.warn : METER.normal

const EIGHTHS = ['', '▏', '▎', '▍', '▌', '▋', '▊', '▉'] as const

// 칸 하나를 8등분해 그린다: 채운 칸(█), 마지막 부분 칸(▏…▉), 빈 칸(공백)
export const meterCells = (percent: number, cells: number) => {
  const eighths = Math.round((Math.min(100, Math.max(0, percent)) / 100) * cells * 8)
  const full = Math.floor(eighths / 8)
  const partial = EIGHTHS[eighths % 8] ?? ''
  const empty = cells - full - (partial === '' ? 0 : 1)
  return { filled: '█'.repeat(full) + partial, empty: ' '.repeat(empty) }
}
