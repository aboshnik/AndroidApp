import { apiClient } from './client'
import type {
  BotProfileResponse,
  ChatMediaUploadResponse,
  ClearThreadHistoryResponse,
  ColleagueSearchResponse,
  DeleteMessageResponse,
  EditMessageRequest,
  EditMessageResponse,
  MessagesResponse,
  OpenDirectThreadRequest,
  OpenDirectThreadResponse,
  SendMessageRequest,
  SendMessageResponse,
  ThreadsResponse,
  EventRegistrantsSearchResponse,
  UpdateBotProfileRequest,
  UpdateBotProfileResponse,
} from './types'

export async function getThreads(login: string) {
  const { data } = await apiClient.get<ThreadsResponse>('api/chat/threads', { params: { login } })
  return data
}

export async function getMessages(threadId: number, login: string, take = 120) {
  const { data } = await apiClient.get<MessagesResponse>(`api/chat/threads/${threadId}/messages`, {
    params: { login, take },
  })
  return data
}

export async function sendMessage(threadId: number, body: SendMessageRequest) {
  const { data } = await apiClient.post<SendMessageResponse>(`api/chat/threads/${threadId}/messages`, body)
  return data
}

export async function editMessage(threadId: number, messageId: number, body: EditMessageRequest) {
  const { data } = await apiClient.put<EditMessageResponse>(`api/chat/threads/${threadId}/messages/${messageId}`, body)
  return data
}

export async function uploadChatMedia(threadId: number, login: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  const { data } = await apiClient.post<ChatMediaUploadResponse>(`api/chat/threads/${threadId}/media`, form, {
    params: { login },
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

export async function searchColleagues(login: string, q = '') {
  const { data } = await apiClient.get<ColleagueSearchResponse>('api/chat/colleagues/search', {
    params: { login, q },
  })
  return data
}

export async function openDirectThread(body: OpenDirectThreadRequest) {
  const { data } = await apiClient.post<OpenDirectThreadResponse>('api/chat/threads/direct/open', body)
  return data
}

export async function deleteMessage(threadId: number, messageId: number, login: string) {
  const { data } = await apiClient.delete<DeleteMessageResponse>(`api/chat/threads/${threadId}/messages/${messageId}`, {
    params: { login },
  })
  return data
}

export async function clearThreadHistory(threadId: number, login: string) {
  const { data } = await apiClient.post<ClearThreadHistoryResponse>(`api/chat/threads/${threadId}/history/clear`, null, {
    params: { login },
  })
  return data
}

export async function getBotProfile(botId: string, login: string) {
  const { data } = await apiClient.get<BotProfileResponse>(`api/chat/bots/${encodeURIComponent(botId)}/profile`, {
    params: { login },
  })
  return data
}

export async function updateBotProfile(botId: string, body: UpdateBotProfileRequest) {
  const { data } = await apiClient.post<UpdateBotProfileResponse>(
    `api/chat/bots/${encodeURIComponent(botId)}/profile`,
    body,
  )
  return data
}

export async function uploadBotAvatar(botId: string, login: string, file: File) {
  const form = new FormData()
  form.append('file', file)
  const { data } = await apiClient.post<UpdateBotProfileResponse>(
    `api/chat/bots/${encodeURIComponent(botId)}/avatar`,
    form,
    {
      params: { login },
      headers: { 'Content-Type': 'multipart/form-data' },
    },
  )
  return data
}

export async function searchEventRegistrantsMentions(q: string, take = 12) {
  const { data } = await apiClient.get<EventRegistrantsSearchResponse>('api/post/event-registrants/search', {
    params: { q, take },
  })
  return data
}
