package com.example.app.api

import com.google.gson.annotations.SerializedName

data class ConfirmDeviceLoginRequest(
    @SerializedName("login") val login: String,
    @SerializedName("password") val password: String,
    @SerializedName("deviceId") val deviceId: String,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("attemptId") val attemptId: Int,
    @SerializedName("code") val code: String
)
