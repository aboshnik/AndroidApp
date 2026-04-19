export type EmployeeLoginRequest = {
  login: string
  password: string
  deviceId: string
  deviceName: string
  reloginBypass?: boolean
}

export type EmployeeLoginResult = {
  lastName: string
  firstName: string
  phone: string
  employeeId: string
  canCreatePosts: boolean
  isTechAdmin: boolean
  canUseDevConsole: boolean
}

export type EmployeeLoginResponse = {
  success: boolean
  message: string
  result: EmployeeLoginResult | null
  requiresDeviceCode: boolean
  pendingAttemptId: number | null
}

export type AuthRegisterRequest = {
  employeeId: string
  phone: string
}

export type AuthRegisterResult = {
  login: string
  employeeId: string
  lastName: string
  firstName: string
  phone: string
  position: string
  subdivision: string
  canCreatePosts: boolean
  isTechAdmin: boolean
  canUseDevConsole: boolean
}

export type AuthRegisterResponse = {
  success: boolean
  message: string
  result: AuthRegisterResult | null
}

export type ConfirmDeviceLoginRequest = {
  login: string
  password: string
  deviceId: string
  deviceName: string
  attemptId: number
  code: string
}

export type EmployeeProfile = {
  lastName: string
  firstName: string
  phone: string
  employeeId: string
  position: string
  subdivision: string
  avatarUrl?: string | null
  level: number
  experience: number
  xpToNext: number
}

export type EmployeeProfileResponse = {
  success: boolean
  message: string
  profile: EmployeeProfile | null
}

export type AvatarUploadResponse = {
  success: boolean
  message: string
  avatarUrl?: string | null
}

export type EmployeeWorkDay = {
  date: string
  dayType: string
  shiftStart?: string | null
  shiftEnd?: string | null
}

export type EmployeeWorkSchedule = {
  workPattern?: string | null
  shiftStart?: string | null
  shiftEnd?: string | null
  vacationStart?: string | null
  vacationEnd?: string | null
}

export type EmployeeWorkScheduleResponse = {
  success: boolean
  message: string
  schedule?: EmployeeWorkSchedule | null
  employeeId?: string
  fullName?: string
  month?: string
  days?: EmployeeWorkDay[] | null
}

export type ThreadItem = {
  id: number
  type: string
  title: string
  botId?: string | null
  createdAtUtc: string
  lastMessageText?: string | null
  lastMessageAtUtc?: string | null
  lastMessageFromSelf: boolean
  lastMessageIsRead: boolean
  unreadCount: number
  isTechAdmin: boolean
  isOfficialBot: boolean
  isOnline: boolean
  avatarUrl?: string | null
}

export type ThreadsResponse = {
  success: boolean
  message: string
  threads: ThreadItem[] | null
}

export type ColleagueItem = {
  login: string
  employeeId: string
  fullName: string
  position: string
  isTechAdmin: boolean
  isOnline: boolean
  avatarUrl?: string | null
}

export type ColleagueSearchResponse = {
  success: boolean
  message: string
  colleagues: ColleagueItem[] | null
}

export type MessageItem = {
  id: number
  senderType: string
  senderId?: string | null
  senderName?: string | null
  text: string
  createdAtUtc: string
  metaJson?: string | null
  senderIsTechAdmin: boolean
  isRead: boolean
  isEdited: boolean
}

export type MessagesResponse = {
  success: boolean
  message: string
  messages: MessageItem[] | null
}

export type SendMessageRequest = {
  login: string
  text: string
  metaJson?: string | null
}

export type SendMessageResponse = {
  success: boolean
  message: string
  item: MessageItem | null
}

export type EditMessageRequest = {
  login: string
  text: string
}

export type EditMessageResponse = {
  success: boolean
  message: string
  item: MessageItem | null
}

export type ChatMediaUploadResponse = {
  success: boolean
  message: string
  url?: string | null
  mime?: string | null
  kind?: string | null
}

export type DeleteMessageResponse = {
  success: boolean
  message: string
}

export type ClearThreadHistoryResponse = {
  success: boolean
  message: string
}

export type OpenDirectThreadRequest = {
  login: string
  colleagueLogin: string
}

export type OpenDirectThreadResponse = {
  success: boolean
  message: string
  thread: ThreadItem | null
}

export type BotProfileItem = {
  botId: string
  displayName: string
  description?: string | null
  avatarUrl?: string | null
  isOfficial: boolean
}

export type BotProfileResponse = {
  success: boolean
  message: string
  profile: BotProfileItem | null
}

export type UpdateBotProfileRequest = {
  login: string
  displayName?: string | null
  description?: string | null
  isOfficial?: boolean | null
}

export type UpdateBotProfileResponse = {
  success: boolean
  message: string
  profile: BotProfileItem | null
}

export type PollCreateRequest = {
  question: string
  description?: string | null
  options: string[]
  allowMediaInQuestionAndOptions?: boolean
  showVoters?: boolean
  allowRevote?: boolean
  shuffleOptions?: boolean
  endsAtUtc?: string | null
  hideResultsUntilEnd?: boolean
  creatorCanViewWithoutVoting?: boolean
}

export type PollVoterItem = {
  login: string
  avatarUrl?: string | null
}

export type PollOptionItem = {
  id: number
  text: string
  votesCount: number
  voters?: PollVoterItem[] | null
}

export type PollItem = {
  question: string
  description?: string | null
  options: PollOptionItem[]
  allowMediaInQuestionAndOptions: boolean
  showVoters: boolean
  allowRevote: boolean
  shuffleOptions: boolean
  endsAtUtc?: string | null
  hideResultsUntilEnd: boolean
  creatorCanViewWithoutVoting: boolean
  totalVotes: number
  hasVoted: boolean
  selectedOptionId?: number | null
  canViewResults: boolean
}

export type PostItem = {
  id: number
  authorLogin: string
  authorName: string
  content: string
  createdAt: string
  imageUrl?: string | null
  mediaUrls?: string[] | null
  isImportant: boolean
  expiresAt?: string | null
  likesCount: number
  commentsCount: number
  poll?: PollItem | null
}

export type CreatePostRequest = {
  content: string
  authorLogin: string
  isImportant: boolean
  poll?: PollCreateRequest | null
}

export type CreatePostResponse = {
  success: boolean
  message: string
  post: PostItem | null
}

export type FeedResponse = {
  success: boolean
  message: string
  posts: PostItem[] | null
}

export type DeletePostResponse = {
  success: boolean
  message: string
}

export type VoteRequest = {
  login: string
  optionId: number
}

export type VoteResponse = {
  success: boolean
  message: string
  poll?: PollItem | null
}
