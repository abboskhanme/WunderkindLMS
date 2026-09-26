import { api, USE_MOCK } from '../client'

/** Boshqaruv → AI ulanishlar (superadmin). Server: `api/admin/mcp` (McpAdminController). */

export interface McpStatus {
  enabled: boolean
  disabledReason: string | null
  endpoint: string
}

export interface McpConnection {
  id: string
  clientId: string
  clientName: string
  userId: string
  userName: string
  userRole: string | null
  userAllowed: boolean
  createdAt: string
  lastUsedAt: string | null
  revokedAt: string | null
  revokedBy: string | null
  active: boolean
  calls: number
  /** Kalit yuboriladigan manzil(lar): host yoki "kompyuteringizdagi dastur". */
  redirectHosts: string[]
}

export interface McpAuditRow {
  id: number
  at: string
  userId: string
  userName: string
  clientName: string
  tool: string
  arguments: string
  outcome: 'ok' | 'denied' | 'error'
  rowCount: number | null
  durationMs: number
}

export interface McpAuditPage {
  total: number
  page: number
  pageSize: number
  rows: McpAuditRow[]
}

export async function getMcpStatus(): Promise<McpStatus> {
  if (USE_MOCK) return { enabled: false, disabledReason: 'mock', endpoint: '' }
  const { data } = await api.get<McpStatus>('/admin/mcp/status')
  return data
}

export async function getMcpConnections(): Promise<McpConnection[]> {
  if (USE_MOCK) return []
  const { data } = await api.get<McpConnection[]>('/admin/mcp/connections')
  return data
}

export async function getMcpAudit(params: {
  userId?: string
  grantId?: string
  outcome?: string
  page?: number
  pageSize?: number
}): Promise<McpAuditPage> {
  if (USE_MOCK) return { total: 0, page: 1, pageSize: 50, rows: [] }
  const { data } = await api.get<McpAuditPage>('/admin/mcp/audit', { params })
  return data
}

export async function revokeMcpConnection(id: string): Promise<void> {
  await api.post(`/admin/mcp/connections/${id}/revoke`)
}
