package com.example.app.api

import com.google.gson.annotations.SerializedName

data class EmployeeLoginRequest(
    @SerializedName("login") val login: String,
    @SerializedName("password") val password: String,
    @SerializedName("deviceId") val deviceId: String,
    @SerializedName("deviceName") val deviceName: String,
    @SerializedName("reloginBypass") val reloginBypass: Boolean = false
)

