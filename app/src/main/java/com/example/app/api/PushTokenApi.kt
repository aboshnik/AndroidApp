package com.example.app.api

import com.google.gson.annotations.SerializedName

data class RegisterPushTokenRequest(
    @SerializedName("login") val login: String?,
    @SerializedName("token") val token: String,
    @SerializedName("deviceId") val deviceId: String,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("platform") val platform: String = "android"
)

data class RegisterPushTokenResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

