package com.example.app.api

import com.google.gson.annotations.SerializedName

data class SetPermissionsRequest(
    @SerializedName("login") val login: String,
    @SerializedName("canCreatePosts") val canCreatePosts: Boolean,
    @SerializedName("canTechAdmin") val canTechAdmin: Boolean = false
)

data class NotifyTestRequest(
    @SerializedName("login") val login: String?
)

data class NotifyUpdateRequest(
    @SerializedName("versionCode") val versionCode: String?,
    @SerializedName("sendPush") val sendPush: Boolean
)

data class AdminActionResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

