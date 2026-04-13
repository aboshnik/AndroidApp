package com.example.app.api

import com.google.gson.annotations.SerializedName

data class ThreadItem(
    @SerializedName("id") val id: Int,
    @SerializedName("type") val type: String,
    @SerializedName("title") val title: String,
    @SerializedName("botId") val botId: String? = null,
    @SerializedName("createdAtUtc") val createdAtUtc: String,
    @SerializedName("lastMessageText") val lastMessageText: String? = null,
    @SerializedName("lastMessageAtUtc") val lastMessageAtUtc: String? = null,
    /** Последнее сообщение от текущего пользователя — в списке чатов показываем ✓ и время. */
    @SerializedName("lastMessageFromSelf") val lastMessageFromSelf: Boolean = false,
    /** Прочитано ли последнее исходящее сообщение собеседником. */
    @SerializedName("lastMessageIsRead") val lastMessageIsRead: Boolean = false,
    @SerializedName("unreadCount") val unreadCount: Int = 0,
    @SerializedName("isTechAdmin") val isTechAdmin: Boolean = false,
    @SerializedName("isOfficialBot") val isOfficialBot: Boolean = false,
    @SerializedName("avatarUrl") val avatarUrl: String? = null
)

data class ThreadsResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("threads") val threads: List<ThreadItem>? = null
)

data class ColleagueItem(
    @SerializedName("login") val login: String,
    @SerializedName("employeeId") val employeeId: String,
    @SerializedName("fullName") val fullName: String,
    @SerializedName("position") val position: String,
    @SerializedName("isTechAdmin") val isTechAdmin: Boolean = false,
    @SerializedName("avatarUrl") val avatarUrl: String? = null
)

data class ColleagueSearchResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("colleagues") val colleagues: List<ColleagueItem>? = null
)

data class MessageItem(
    @SerializedName("id") val id: Int,
    @SerializedName("senderType") val senderType: String,
    @SerializedName("senderId") val senderId: String? = null,
    @SerializedName("senderName") val senderName: String? = null,
    @SerializedName("text") val text: String,
    @SerializedName("createdAtUtc") val createdAtUtc: String,
    @SerializedName("metaJson") val metaJson: String? = null,
    @SerializedName("senderIsTechAdmin") val senderIsTechAdmin: Boolean = false,
    @SerializedName("isRead") val isRead: Boolean = false
)

data class MessagesResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("messages") val messages: List<MessageItem>? = null
)

data class SendMessageRequest(
    @SerializedName("login") val login: String,
    @SerializedName("text") val text: String,
    @SerializedName("metaJson") val metaJson: String? = null
)

data class SendMessageResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("item") val item: MessageItem? = null
)

data class ChatMediaUploadResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("url") val url: String? = null,
    @SerializedName("mime") val mime: String? = null,
    @SerializedName("kind") val kind: String? = null
)

data class DeleteMessageResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

data class ClearThreadHistoryResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

data class OpenDirectThreadRequest(
    @SerializedName("login") val login: String,
    @SerializedName("colleagueLogin") val colleagueLogin: String
)

data class OpenDirectThreadResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("thread") val thread: ThreadItem? = null
)

data class BotProfileItem(
    @SerializedName("botId") val botId: String,
    @SerializedName("displayName") val displayName: String,
    @SerializedName("description") val description: String? = null,
    @SerializedName("avatarUrl") val avatarUrl: String? = null,
    @SerializedName("isOfficial") val isOfficial: Boolean = false
)

data class BotProfileResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("profile") val profile: BotProfileItem? = null
)

data class UpdateBotProfileRequest(
    @SerializedName("login") val login: String,
    @SerializedName("displayName") val displayName: String? = null,
    @SerializedName("description") val description: String? = null,
    @SerializedName("isOfficial") val isOfficial: Boolean? = null
)

data class UpdateBotProfileResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("profile") val profile: BotProfileItem? = null
)

