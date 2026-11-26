// Ported from graphiti_core/prompts/names.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Prompts;

// Keep in sync with PROMPT_GROUPS / BUILTIN_PROMPT_SPECS in lib.py.
// TODO(port): no rule for a module-level type alias of `Literal[...]`. The module holds only the two aliases
// `PromptGroup` (8 group names) and `PromptName` (25 prompt names), and no code reads their values at run time. By
// the `Literal` row of R7 each annotation `PromptName` or `PromptGroup` is `string` in C#, so this file declares no
// type. The other form is a static class `Names` with the two value lists.
