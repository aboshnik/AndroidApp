package com.example.app.api

import com.google.gson.annotations.SerializedName

data class CreatePostRequest(
    @SerializedName("content") val content: String,
    @SerializedName("authorLogin") val authorLogin: String,
    @SerializedName("isImportant") val isImportant: Boolean
)

data class PostItem(
    @SerializedName("id") val id: Int,
    @SerializedName("authorLogin") val authorLogin: String,
    @SerializedName("authorName") val authorName: String,
    @SerializedName("content") val content: String,
    @SerializedName("createdAt") val createdAt: String,
    @SerializedName("imageUrl") val imageUrl: String?,
    @SerializedName("isImportant") val isImportant: Boolean,
    @SerializedName("expiresAt") val expiresAt: String?,
    @SerializedName("likesCount") val likesCount: Int,
    @SerializedName("commentsCount") val commentsCount: Int
)

data class CreatePostResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("post") val post: PostItem?
)

data class FeedResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String,
    @SerializedName("posts") val posts: List<PostItem>?
)

data class DeletePostResponse(
    @SerializedName("success") val success: Boolean,
    @SerializedName("message") val message: String
)
