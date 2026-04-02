package com.example.app.api

import com.google.gson.annotations.SerializedName

data class SecurityDecisionRequest(
    @SerializedName("login") val login: String,
    @SerializedName("attemptId") val attemptId: Int
)

data class SecurityDecisionResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)

