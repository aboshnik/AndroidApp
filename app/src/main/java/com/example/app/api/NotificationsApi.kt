package com.example.app.api

import com.google.gson.annotations.SerializedName

data class NotificationItem(
    @SerializedName("id") val id: Int,
    @SerializedName("type") val type: String,
    @SerializedName("title") val title: String,
    @SerializedName("body") val body: String,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("action") val action: String?,
    @SerializedName("actionData") val actionData: String?,
    @SerializedName("isRead") val isRead: Boolean
)

data class NotificationsResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("unreadCount") val unreadCount: Int,
    @SerializedName("items") val items: List<NotificationItem>?
)

data class MarkReadRequest(
    @SerializedName("login") val login: String
)

data class MarkReadResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

