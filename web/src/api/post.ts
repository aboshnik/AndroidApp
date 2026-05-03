import { apiClient } from './client'
import type {
  CreatePostRequest,
  CreatePostResponse,
  DeletePostResponse,
  EventRegisterRequest,
  EventRegisterResponse,
  FeedResponse,
  PollCreateRequest,
  VoteRequest,
  VoteResponse,
} from './types'

export async function getFeed(login?: string) {
  const { data } = await apiClient.get<FeedResponse>('api/post/feed', {
    params: { login: login?.trim() || undefined },
  })
  return data
}

export async function createPost(body: CreatePostRequest) {
  const { data } = await apiClient.post<CreatePostResponse>('api/post', body)
  return data
}

export async function createPostWithMedia(input: {
  content: string
  authorLogin: string
  isImportant: boolean
  poll?: PollCreateRequest | null
  isEvent?: boolean
  files: File[]
}) {
  const form = new FormData()
  form.append('content', input.content)
  form.append('authorLogin', input.authorLogin)
  form.append('isImportant', String(input.isImportant))
  if (input.isEvent) {
    form.append('isEvent', 'true')
  }
  if (input.poll) form.append('pollJson', JSON.stringify(input.poll))
  for (const file of input.files) form.append('media', file)
  const { data } = await apiClient.post<CreatePostResponse>('api/post/media', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

export async function deletePost(id: number, login: string) {
  const { data } = await apiClient.delete<DeletePostResponse>(`api/post/${id}`, { params: { login } })
  return data
}

export async function votePost(id: number, body: VoteRequest) {
  const { data } = await apiClient.post<VoteResponse>(`api/post/${id}/vote`, body)
  return data
}

export async function registerEvent(id: number, body: EventRegisterRequest) {
  const { data } = await apiClient.post<EventRegisterResponse>(`api/post/${id}/register-event`, body)
  return data
}
