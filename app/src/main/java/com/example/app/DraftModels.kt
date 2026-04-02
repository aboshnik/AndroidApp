package com.example.app

data class Draft(
    val id: Long,
    val content: String,
    val attachmentUris: List<String>,
    val updatedAt: Long
)

