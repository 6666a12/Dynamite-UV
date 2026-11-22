# Run only after a successful link. Reconfiguration alone must not relabel an old DLL.
if(NOT DEFINED UV_DLL OR NOT EXISTS "${UV_DLL}" OR NOT UV_VARIANT MATCHES "^(qa|release)$")
    message(FATAL_ERROR "Expected the linked UV_DLL and its qa/release build variant")
endif()
get_filename_component(output "${UV_DLL}" DIRECTORY)
file(SHA256 "${UV_DLL}" digest)
set(manifest "${output}/uv_video_stream.variant")
file(WRITE "${manifest}.tmp" "${UV_VARIANT}\n${digest}\n")
file(RENAME "${manifest}.tmp" "${manifest}")
